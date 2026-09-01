using Microsoft.EntityFrameworkCore;
using StockTrader.Application.TradingCore;
using StockTrader.Models;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Data.Repositories;

/// <summary>
/// Owns Edge's durable half of an authority handoff. The in-process lease closes the race between
/// a scheduler deciding to start and the coordinator persisting the barrier request.
/// </summary>
public sealed class EdgeFinancialAuthorityControl(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider clock) : IEdgeFinancialAuthorityControl, IFinancialCycleBarrier
{
    private readonly object _positionSync = new();
    private int _activePositionCycles;
    private bool _positionBarrierRequested;
    private TaskCompletionSource _positionCyclesDrained = CompletedSignal();

    public async Task<IAsyncDisposable?> TryEnterPositionCycleAsync(CancellationToken ct = default)
    {
        lock (_positionSync)
        {
            if (_positionBarrierRequested)
                return null;
            _activePositionCycles++;
            if (_activePositionCycles == 1)
                _positionCyclesDrained = NewSignal();
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var blocked = await db.FinancialAuthorityFences.AsNoTracking()
                .AnyAsync(value => !value.IsReleased
                    && (value.PositionCycle == FinancialPositionCycleStates.Finishing
                        || value.PositionCycle == FinancialPositionCycleStates.AtBarrier), ct);
            if (blocked)
            {
                ExitPositionCycle();
                return null;
            }
            return new PositionCycleLease(this);
        }
        catch
        {
            ExitPositionCycle();
            throw;
        }
    }

    public async Task<AuthorityFenceReceipt> FenceAsync(
        string transitionId, long authorityGeneration, CancellationToken ct = default)
    {
        ValidateIdentity(transitionId, authorityGeneration);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var latestGeneration = await db.FinancialAuthorityFences
            .MaxAsync(value => (long?)value.AuthorityGeneration, ct) ?? 0;
        var fence = await db.FinancialAuthorityFences
            .SingleOrDefaultAsync(value => value.TransitionId == transitionId, ct);
        if (fence is not null)
        {
            if (fence.AuthorityGeneration != authorityGeneration)
                throw new InvalidOperationException("edge-authority-transition-identity-conflict");
            return Receipt(fence);
        }
        if (authorityGeneration < latestGeneration)
            throw new InvalidOperationException("edge-authority-generation-regression");

        fence = new FinancialAuthorityFence
        {
            TransitionId = transitionId,
            AuthorityGeneration = authorityGeneration,
            NewEntryAcceptance = AuthorityCommandAcceptanceStates.Fenced,
            ManualCommandAcceptance = AuthorityCommandAcceptanceStates.Fenced,
            PositionCycle = FinancialPositionCycleStates.Active,
            EntryReconciliation = FinancialReconciliationStates.Draining,
            PositionReconciliation = FinancialReconciliationStates.Draining,
            FenceHash = string.Empty,
            UpdatedAtUtc = UtcNow(),
        };
        await RefreshInventoryAsync(db, fence, ct);
        Seal(fence);
        db.FinancialAuthorityFences.Add(fence);
        await db.SaveChangesAsync(ct);
        return Receipt(fence);
    }

    public async Task<AuthorityFenceReceipt> EnterPositionBarrierAsync(
        string transitionId, long authorityGeneration, CancellationToken ct = default)
    {
        ValidateIdentity(transitionId, authorityGeneration);
        Task waitForCycles;
        lock (_positionSync)
        {
            _positionBarrierRequested = true;
            waitForCycles = _positionCyclesDrained.Task;
        }

        await SetPositionStateAsync(
            transitionId, authorityGeneration, FinancialPositionCycleStates.Finishing, ct);
        await waitForCycles.WaitAsync(ct);
        return await SetPositionStateAsync(
            transitionId, authorityGeneration, FinancialPositionCycleStates.AtBarrier, ct);
    }

    public async Task<AuthorityDrainInventory> ReadDrainInventoryAsync(
        string transitionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fence = await RequiredFenceAsync(db, transitionId, ct);
        await RefreshInventoryAsync(db, fence, ct);
        if (fence.UnresolvedIntentCount == 0 && fence.UnresolvedBrokerEffectCount == 0)
        {
            fence.EntryReconciliation = FinancialReconciliationStates.Clear;
            fence.PositionReconciliation = FinancialReconciliationStates.Clear;
        }
        Seal(fence);
        await db.SaveChangesAsync(ct);
        var observedAt = UtcNow();
        var inventory = new AuthorityDrainInventory(
            fence.UnresolvedIntentCount, fence.UnresolvedBrokerEffectCount, 0,
            fence.ActivityJournalCount, fence.EnabledConsumerLag, observedAt, string.Empty);
        return inventory with { InventoryHash = TradingControlIdentity.Drain(inventory) };
    }

    public async Task MirrorAuthorityAsync(
        string transitionId, long authorityGeneration, string mode, string owner,
        string receiptHash, CancellationToken ct = default)
    {
        ValidateIdentity(transitionId, authorityGeneration);
        if (!Enum.TryParse<TradingAuthorityMode>(mode, false, out var parsedMode)
            || AuthorityOwners.ForMode(parsedMode) != owner
            || string.IsNullOrWhiteSpace(receiptHash))
            throw new ArgumentException("invalid-edge-authority-mirror");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var mirror = await db.FinancialAuthorityMirrors.SingleOrDefaultAsync(value => value.Id == 1, ct);
        if (mirror is not null && mirror.AuthorityGeneration > authorityGeneration)
            throw new InvalidOperationException("edge-authority-generation-regression");
        if (mirror is not null && mirror.AuthorityGeneration == authorityGeneration)
        {
            if (mirror.TransitionId != transitionId || mirror.ReceiptHash != receiptHash
                || mirror.Mode != mode || mirror.Owner != owner)
                throw new InvalidOperationException("edge-authority-mirror-conflict");
            return;
        }
        mirror ??= new FinancialAuthorityMirror { Id = 1, Mode = mode, Owner = owner,
            TransitionId = transitionId, ReceiptHash = receiptHash };
        mirror.AuthorityGeneration = authorityGeneration;
        mirror.Mode = mode;
        mirror.Owner = owner;
        mirror.TransitionId = transitionId;
        mirror.ReceiptHash = receiptHash;
        mirror.MirroredAtUtc = UtcNow();
        if (db.Entry(mirror).State == EntityState.Detached)
            db.FinancialAuthorityMirrors.Add(mirror);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthorityFenceReceipt> ReleaseAsync(
        string transitionId, long authorityGeneration, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fence = await RequiredFenceAsync(db, transitionId, ct);
        if (fence.AuthorityGeneration != authorityGeneration)
            throw new InvalidOperationException("edge-authority-generation-mismatch");
        var mirror = await db.FinancialAuthorityMirrors.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == 1, ct);
        if (mirror is null || mirror.AuthorityGeneration <= authorityGeneration
            || mirror.Owner != AuthorityOwners.Edge)
            throw new InvalidOperationException("edge-higher-local-authority-not-mirrored");
        fence.NewEntryAcceptance = AuthorityCommandAcceptanceStates.Open;
        fence.ManualCommandAcceptance = AuthorityCommandAcceptanceStates.Open;
        fence.PositionCycle = FinancialPositionCycleStates.Active;
        fence.EntryReconciliation = FinancialReconciliationStates.Active;
        fence.PositionReconciliation = FinancialReconciliationStates.Active;
        fence.IsReleased = true;
        fence.UpdatedAtUtc = UtcNow();
        Seal(fence);
        await db.SaveChangesAsync(ct);
        lock (_positionSync) _positionBarrierRequested = false;
        return Receipt(fence);
    }

    private async Task<AuthorityFenceReceipt> SetPositionStateAsync(
        string transitionId, long authorityGeneration, string state, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fence = await RequiredFenceAsync(db, transitionId, ct);
        if (fence.AuthorityGeneration != authorityGeneration || fence.IsReleased)
            throw new InvalidOperationException("edge-authority-generation-mismatch");
        fence.PositionCycle = state;
        fence.UpdatedAtUtc = UtcNow();
        Seal(fence);
        await db.SaveChangesAsync(ct);
        return Receipt(fence);
    }

    private static async Task RefreshInventoryAsync(
        AppDbContext db, FinancialAuthorityFence fence, CancellationToken ct)
    {
        var unresolvedEntries = await db.TradeRecommendations.AsNoTracking().CountAsync(item =>
            !item.IsSuperseded && !item.WasExecuted && item.EntryRequestedAt != null, ct);
        var unresolvedPositions = await db.Positions.AsNoTracking().CountAsync(item =>
            item.ClosedAt == null && item.ExecutionRequestedAt != null, ct);
        var entryEffects = await db.TradeRecommendations.AsNoTracking().CountAsync(item =>
            !item.IsSuperseded && !item.WasExecuted && item.EntryRequestedAt != null
            && item.EntryOrderId != null, ct);
        var positionEffects = await db.Positions.AsNoTracking().CountAsync(item =>
            item.ClosedAt == null && item.ExecutionRequestedAt != null
            && item.ExecutionOrderId != null, ct);
        fence.UnresolvedIntentCount = unresolvedEntries + unresolvedPositions;
        fence.UnresolvedBrokerEffectCount = entryEffects + positionEffects;
        fence.ActivityJournalCount = 0;
        fence.EnabledConsumerLag = 0;
    }

    private static async Task<FinancialAuthorityFence> RequiredFenceAsync(
        AppDbContext db, string transitionId, CancellationToken ct) =>
        await db.FinancialAuthorityFences.SingleOrDefaultAsync(
            value => value.TransitionId == transitionId, ct)
        ?? throw new InvalidOperationException("edge-authority-fence-not-found");

    private static void Seal(FinancialAuthorityFence fence)
    {
        var receipt = Receipt(fence) with { FenceHash = string.Empty };
        fence.FenceHash = TradingControlIdentity.Fence(receipt);
    }

    private static AuthorityFenceReceipt Receipt(FinancialAuthorityFence fence) => new(
        AuthorityOwners.Edge, fence.AuthorityGeneration,
        fence.NewEntryAcceptance, fence.ManualCommandAcceptance, fence.PositionCycle,
        fence.EntryReconciliation, fence.PositionReconciliation,
        fence.LastCompletedPositionBarUtc, fence.UnresolvedIntentCount,
        fence.UnresolvedBrokerEffectCount, fence.ActivityJournalCount,
        fence.EnabledConsumerLag, fence.FenceHash);

    private static void ValidateIdentity(string transitionId, long authorityGeneration)
    {
        if (!Guid.TryParse(transitionId, out _) || authorityGeneration < 1)
            throw new ArgumentException("invalid-edge-authority-transition");
    }

    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSignal()
    {
        var signal = NewSignal();
        signal.SetResult();
        return signal;
    }

    private void ExitPositionCycle()
    {
        lock (_positionSync)
        {
            if (--_activePositionCycles == 0)
                _positionCyclesDrained.TrySetResult();
        }
    }

    private sealed class PositionCycleLease(EdgeFinancialAuthorityControl owner) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ExitPositionCycle();
            return ValueTask.CompletedTask;
        }
    }
}

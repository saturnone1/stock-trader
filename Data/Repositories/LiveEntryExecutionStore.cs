using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class LiveEntryExecutionStore(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache)
    : ILiveEntryExecutionStore
{
    public async Task<bool> TryClaimAsync(
        TradeRecommendation recommendation,
        int accountId,
        DateTime requestedAt,
        CancellationToken ct = default)
    {
        if (recommendation.Id <= 0 || accountId <= 0)
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.TradeRecommendations
            .Where(item => item.Id == recommendation.Id
                && !item.IsSuperseded
                && !item.WasExecuted
                && item.EntryRequestedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EntryRequestedAt, requestedAt)
                .SetProperty(item => item.EntryAccountId, accountId)
                .SetProperty(item => item.EntryOrderId, (string?)null)
                .SetProperty(item => item.EntryExecutionNote, (string?)null), ct);
        if (updated > 0)
            TradeReadCache.InvalidateRecommendations(cache);
        return updated == 1;
    }

    public async Task<bool> SetOrderEvidenceAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        string orderId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.TradeRecommendations
            .Where(item => item.Id == recommendation.Id
                && !item.WasExecuted
                && item.EntryRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EntryOrderId, orderId), ct);
        if (updated > 0)
            TradeReadCache.InvalidateRecommendations(cache);
        return updated == 1;
    }

    public async Task<bool> ReleaseClaimAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        string note,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.TradeRecommendations
            .Where(item => item.Id == recommendation.Id
                && !item.WasExecuted
                && item.EntryRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EntryRequestedAt, (DateTime?)null)
                .SetProperty(item => item.EntryExecutionNote, note), ct);
        if (updated > 0)
            TradeReadCache.InvalidateRecommendations(cache);
        return updated == 1;
    }

    public async Task<bool> SetExecutionNoteAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        string note,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.TradeRecommendations
            .Where(item => item.Id == recommendation.Id
                && !item.WasExecuted
                && item.EntryRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.EntryExecutionNote, note), ct);
        if (updated > 0)
            TradeReadCache.InvalidateRecommendations(cache);
        return updated == 1;
    }

    public async Task<bool> CommitFilledEntryAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        Position position,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.TradeRecommendations
            .Where(item => item.Id == recommendation.Id
                && !item.WasExecuted
                && item.EntryRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.WasExecuted, true)
                .SetProperty(item => item.EntryExecutionNote, (string?)null), ct);
        if (updated != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }

        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        TradeReadCache.InvalidateRecommendations(cache);
        return true;
    }

    public async Task<TradeRecommendation?> LoadAsync(
        long recommendationId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeRecommendations.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == recommendationId && !item.IsSuperseded,
                ct);
    }

    public async Task<IReadOnlyList<TradeRecommendation>> LoadPendingAsync(
        int count = 100,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeRecommendations.AsNoTracking()
            .Where(item => !item.IsSuperseded
                && !item.WasExecuted
                && item.EntryRequestedAt != null)
            .OrderBy(item => item.EntryRequestedAt)
            .Take(Math.Clamp(count, 1, 500))
            .ToListAsync(ct);
    }
}

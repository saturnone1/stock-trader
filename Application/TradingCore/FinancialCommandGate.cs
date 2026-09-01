using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Application.TradingCore;

public static class FinancialCommandClasses
{
    public const string NewEntry = "NewEntry";
    public const string ManualCommand = "ManualCommand";
}

public interface IFinancialCommandGate
{
    Task EnsureOpenAsync(string commandClass, CancellationToken ct = default);
}

public interface IFinancialCycleBarrier
{
    Task<IAsyncDisposable?> TryEnterPositionCycleAsync(CancellationToken ct = default);
}

public interface IEdgeFinancialAuthorityControl
{
    Task<AuthorityFenceReceipt> FenceAsync(string transitionId, long authorityGeneration,
        CancellationToken ct = default);
    Task<AuthorityFenceReceipt> EnterPositionBarrierAsync(string transitionId,
        long authorityGeneration, CancellationToken ct = default);
    Task<AuthorityDrainInventory> ReadDrainInventoryAsync(string transitionId,
        CancellationToken ct = default);
    Task MirrorAuthorityAsync(string transitionId, long authorityGeneration, string mode,
        string owner, string receiptHash, CancellationToken ct = default);
    Task<AuthorityFenceReceipt> ReleaseAsync(string transitionId, long authorityGeneration,
        CancellationToken ct = default);
}

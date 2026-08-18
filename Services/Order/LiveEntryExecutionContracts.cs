using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Order;

public enum LiveEntryExecutionStatus
{
    Rejected,
    Completed,
    AwaitingBroker,
    SubmissionUnconfirmed,
    AlreadyPending,
    AlreadyCompleted,
    EvidenceMismatch,
    AmbiguousEvidence,
    ConcurrentChange,
}

public sealed record LiveEntryExecutionResult(
    LiveEntryExecutionStatus Status,
    BrokerOrder? Order = null,
    Position? Position = null,
    string? Error = null)
{
    public bool ShouldPreventRetry => Status != LiveEntryExecutionStatus.Rejected;
    public bool IsTracked => Status == LiveEntryExecutionStatus.Completed;
}

public interface ILiveEntryExecutionCoordinator
{
    Task<LiveEntryExecutionResult> ExecuteAsync(
        TradeRecommendation recommendation,
        AccountBrokerContext account,
        CancellationToken ct = default);

    Task<LiveEntryExecutionResult> ReconcileAsync(
        TradeRecommendation recommendation,
        AccountBrokerContext account,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default);
}

using StockTrader.Models;

namespace StockTrader.Application.Execution;

public enum LiveEntryOrderState
{
    Ready,
    SubmissionUnconfirmed,
    AwaitingBroker,
    Completed,
    Failed,
}

public sealed record LiveEntryOrderStatus(
    LiveEntryOrderState State,
    DateTime? RequestedAt,
    int? AccountId,
    bool HasBrokerOrderId,
    long PendingSeconds,
    string? Note);

public static class LiveEntryOrderStatusPolicy
{
    public static LiveEntryOrderStatus Evaluate(
        TradeRecommendation recommendation,
        DateTime utcNow)
    {
        if (recommendation.WasExecuted)
        {
            return new LiveEntryOrderStatus(
                LiveEntryOrderState.Completed,
                recommendation.EntryRequestedAt,
                recommendation.EntryAccountId,
                !string.IsNullOrWhiteSpace(recommendation.EntryOrderId),
                0,
                null);
        }

        if (!recommendation.EntryRequestedAt.HasValue)
        {
            return new LiveEntryOrderStatus(
                string.IsNullOrWhiteSpace(recommendation.EntryExecutionNote)
                    ? LiveEntryOrderState.Ready
                    : LiveEntryOrderState.Failed,
                null,
                recommendation.EntryAccountId,
                !string.IsNullOrWhiteSpace(recommendation.EntryOrderId),
                0,
                recommendation.EntryExecutionNote);
        }

        var elapsed = utcNow - recommendation.EntryRequestedAt.Value;
        var hasOrderId = !string.IsNullOrWhiteSpace(recommendation.EntryOrderId);
        return new LiveEntryOrderStatus(
            hasOrderId
                ? LiveEntryOrderState.AwaitingBroker
                : LiveEntryOrderState.SubmissionUnconfirmed,
            recommendation.EntryRequestedAt,
            recommendation.EntryAccountId,
            hasOrderId,
            Math.Max(0, (long)elapsed.TotalSeconds),
            recommendation.EntryExecutionNote);
    }
}

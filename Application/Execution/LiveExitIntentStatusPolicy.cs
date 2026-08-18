using StockTrader.Models;

namespace StockTrader.Application.Execution;

public enum LiveExitIntentState
{
    Ready,
    SubmissionUnconfirmed,
    AwaitingBroker,
}

public sealed record LiveExitIntentStatus(
    LiveExitIntentState State,
    DateTime? RequestedAt,
    string? Reason,
    bool HasBrokerOrderId,
    long PendingSeconds,
    int RequestedQuantity,
    bool MarksPartialProfit);

/// <summary>저장된 청산 의도를 운영 화면에서 이해할 수 있는 상태로 변환합니다.</summary>
public static class LiveExitIntentStatusPolicy
{
    public static LiveExitIntentStatus Evaluate(Position position, DateTime utcNow)
    {
        if (!position.ExecutionRequestedAt.HasValue)
            return new LiveExitIntentStatus(
                LiveExitIntentState.Ready, null, null, false, 0, 0, false);

        var hasOrderId = !string.IsNullOrWhiteSpace(position.ExecutionOrderId);
        var elapsed = utcNow - position.ExecutionRequestedAt.Value;
        return new LiveExitIntentStatus(
            hasOrderId
                ? LiveExitIntentState.AwaitingBroker
                : LiveExitIntentState.SubmissionUnconfirmed,
            position.ExecutionRequestedAt,
            position.ExecutionRequestReason,
            hasOrderId,
            Math.Max(0, (long)elapsed.TotalSeconds),
            position.ExecutionRequestQuantity ?? position.Quantity,
            position.ExecutionRequestMarksPartialProfit);
    }
}

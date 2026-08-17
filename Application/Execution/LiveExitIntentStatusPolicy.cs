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
    long PendingSeconds);

/// <summary>저장된 청산 의도를 운영 화면에서 이해할 수 있는 상태로 변환합니다.</summary>
public static class LiveExitIntentStatusPolicy
{
    public static LiveExitIntentStatus Evaluate(Position position, DateTime utcNow)
    {
        if (!position.ExitRequestedAt.HasValue)
            return new LiveExitIntentStatus(LiveExitIntentState.Ready, null, null, false, 0);

        var hasOrderId = !string.IsNullOrWhiteSpace(position.ExitOrderId);
        var elapsed = utcNow - position.ExitRequestedAt.Value;
        return new LiveExitIntentStatus(
            hasOrderId
                ? LiveExitIntentState.AwaitingBroker
                : LiveExitIntentState.SubmissionUnconfirmed,
            position.ExitRequestedAt,
            position.ExitRequestReason,
            hasOrderId,
            Math.Max(0, (long)elapsed.TotalSeconds));
    }
}

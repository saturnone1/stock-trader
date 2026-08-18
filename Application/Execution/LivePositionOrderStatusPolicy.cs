using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

public enum LivePositionOrderState
{
    Ready,
    SubmissionUnconfirmed,
    AwaitingBroker,
}

public sealed record LivePositionOrderStatus(
    LivePositionOrderState State,
    DateTime? RequestedAt,
    string? Reason,
    PositionExecutionKind? Kind,
    bool HasBrokerOrderId,
    long PendingSeconds,
    int RequestedQuantity,
    bool MarksPartialProfit);

/// <summary>저장된 포지션 주문 의도를 운영 화면에서 이해할 수 있는 상태로 변환합니다.</summary>
public static class LivePositionOrderStatusPolicy
{
    public static LivePositionOrderStatus Evaluate(Position position, DateTime utcNow)
    {
        if (!position.ExecutionRequestedAt.HasValue)
            return new LivePositionOrderStatus(
                LivePositionOrderState.Ready, null, null, null, false, 0, 0, false);

        var hasOrderId = !string.IsNullOrWhiteSpace(position.ExecutionOrderId);
        var elapsed = utcNow - position.ExecutionRequestedAt.Value;
        return new LivePositionOrderStatus(
            hasOrderId
                ? LivePositionOrderState.AwaitingBroker
                : LivePositionOrderState.SubmissionUnconfirmed,
            position.ExecutionRequestedAt,
            position.ExecutionRequestReason,
            position.ExecutionRequestKind ?? PositionExecutionKind.FullExit,
            hasOrderId,
            Math.Max(0, (long)elapsed.TotalSeconds),
            position.ExecutionRequestQuantity ?? position.Quantity,
            position.ExecutionRequestMarksPartialProfit);
    }
}

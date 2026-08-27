using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class TradingPositionCommandStatePolicy
{
    public static TradingPositionProjection MarkRequested(
        TradingPositionProjection position,
        TradingPositionCommand command) => position with
    {
        ExecutionRequestedAtUtc = command.Envelope.OccurredAtUtc,
        ExecutionRequestReason = command.Reason,
        ExecutionRequestQuantity = command.Quantity,
        ExecutionRequestMarksPartialProfit = command.MarksPartialProfit,
        ExecutionRequestKind = command.Action,
        ExecutionRequestRuleIndex = command.ScalingRuleIndex,
        ExecutionOrderId = null,
        ExecutionContext = position.ExecutionContext,
    };

    public static TradingPositionProjection ClearRequest(
        TradingPositionProjection position) => position with
    {
        ExecutionRequestedAtUtc = null,
        ExecutionRequestReason = null,
        ExecutionRequestQuantity = null,
        ExecutionRequestMarksPartialProfit = false,
        ExecutionRequestKind = null,
        ExecutionRequestRuleIndex = null,
        ExecutionOrderId = null,
        ExecutionContext = position.ExecutionContext,
    };
}

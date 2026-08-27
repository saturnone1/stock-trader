using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public sealed record TradingPositionSettlement(
    TradingPositionProjection Position,
    TradingTradeProjection? Trade);

public static class TradingPositionSettlementPolicy
{
    public static TradingPositionSettlement ApplyTerminalOrder(
        TradingPositionProjection position,
        TradingPositionCommand command,
        Broker.BrokerOrderEvidence evidence,
        DateTime observedAtUtc)
    {
        if (!IsCompatibleTerminalFill(evidence.Status, evidence.FilledQuantity, command.Quantity)
            || evidence.AverageFillPrice is not > 0m
            || observedAtUtc == default)
            throw new ArgumentException("Broker evidence is not a compatible terminal position fill.", nameof(evidence));
        if (position.ClosedAtUtc.HasValue || position.Quantity <= 0)
            throw new InvalidOperationException("Position is already closed.");

        var fill = evidence.AverageFillPrice.Value;
        if (command.Action == TradingPositionActionKinds.ScaleIn)
        {
            var nextQuantity = checked(position.Quantity + evidence.FilledQuantity);
            var average = ((position.EntryPrice * position.Quantity) + (fill * evidence.FilledQuantity))
                / nextQuantity;
            return new TradingPositionSettlement(TradingPositionCommandStatePolicy.ClearRequest(position with
            {
                Quantity = nextQuantity,
                EntryPrice = average,
                CurrentPrice = fill,
                ScalingExecutions = RegisterScale(position.ScalingExecutions, command.ScalingRuleIndex),
                ExecutionContext = position.ExecutionContext,
            }), null);
        }

        if (evidence.FilledQuantity > position.Quantity)
            throw new InvalidOperationException("Exit quantity exceeds the open position.");
        var remaining = position.Quantity - evidence.FilledQuantity;
        var filledAt = Utc(evidence.FilledAtUtc ?? observedAtUtc);
        var closed = remaining == 0;
        var updated = TradingPositionCommandStatePolicy.ClearRequest(position with
        {
            Quantity = remaining,
            CurrentPrice = fill,
            ClosedAtUtc = closed ? filledAt : null,
            ExitPrice = closed ? fill : null,
            PartialProfitTaken = position.PartialProfitTaken || command.MarksPartialProfit,
            ScalingExecutions = command.Action == TradingPositionActionKinds.ScaleOut
                ? RegisterScale(position.ScalingExecutions, command.ScalingRuleIndex)
                : position.ScalingExecutions,
            ExecutionContext = position.ExecutionContext,
        });
        var pnl = (fill - position.EntryPrice) * evidence.FilledQuantity;
        var trade = new TradingTradeProjection(
            $"trade:{command.Envelope.CommandId}", position.SourceSignalId, position.Symbol,
            position.PatternCode, position.CustomPatternName, position.EntryPrice, fill,
            evidence.FilledQuantity, position.OpenedAtUtc, filledAt, pnl,
            position.EntryPrice > 0 ? (fill - position.EntryPrice) / position.EntryPrice : 0m,
            command.Reason);
        return new TradingPositionSettlement(updated, trade);
    }

    private static IReadOnlyList<TradingScalingProjection> RegisterScale(
        IReadOnlyList<TradingScalingProjection> values,
        int? ruleIndex)
    {
        if (!ruleIndex.HasValue) return values;
        var result = values.ToDictionary(item => item.RuleIndex, item => item.ExecutionCount);
        result[ruleIndex.Value] = result.GetValueOrDefault(ruleIndex.Value) + 1;
        return result.OrderBy(item => item.Key)
            .Select(item => new TradingScalingProjection(item.Key, item.Value)).ToArray();
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static bool IsCompatibleTerminalFill(string status, int filled, int requested) =>
        string.Equals(status, "Filled", StringComparison.Ordinal)
            ? filled == requested
            : status is "Rejected" or "Cancelled" or "Expired"
                && filled > 0 && filled <= requested;
}

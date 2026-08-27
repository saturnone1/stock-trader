using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public sealed record TradingPositionSettlement(
    TradingPositionProjection Position,
    TradingTradeProjection? Trade);

public static class TradingPositionSettlementPolicy
{
    public static TradingPositionSettlement ApplyFilledOrder(
        TradingPositionProjection position,
        TradingPositionCommand command,
        Broker.BrokerOrderEvidence evidence)
    {
        if (!string.Equals(evidence.Status, "Filled", StringComparison.Ordinal)
            || evidence.FilledQuantity != command.Quantity
            || evidence.AverageFillPrice is not > 0m
            || evidence.FilledAtUtc is null)
            throw new ArgumentException("Broker evidence is not a complete position fill.", nameof(evidence));
        if (position.ClosedAtUtc.HasValue || position.Quantity <= 0)
            throw new InvalidOperationException("Position is already closed.");

        var fill = evidence.AverageFillPrice.Value;
        if (command.Action == TradingPositionActionKinds.ScaleIn)
        {
            var nextQuantity = checked(position.Quantity + command.Quantity);
            var average = ((position.EntryPrice * position.Quantity) + (fill * command.Quantity))
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

        if (command.Quantity > position.Quantity)
            throw new InvalidOperationException("Exit quantity exceeds the open position.");
        var remaining = position.Quantity - command.Quantity;
        var filledAt = Utc(evidence.FilledAtUtc.Value);
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
        var pnl = (fill - position.EntryPrice) * command.Quantity;
        var trade = new TradingTradeProjection(
            $"trade:{command.Envelope.CommandId}", position.SourceSignalId, position.Symbol,
            position.PatternCode, position.CustomPatternName, position.EntryPrice, fill,
            command.Quantity, position.OpenedAtUtc, filledAt, pnl,
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
}

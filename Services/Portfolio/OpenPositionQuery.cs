using StockTrader.Application.Execution;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Trading;

namespace StockTrader.Services.Portfolio;

public sealed class OpenPositionQuery(
    IOpenPositionStore positions,
    TimeProvider timeProvider)
    : IOpenPositionQuery
{
    public async Task<OpenPositionListSnapshot> GetAsync(CancellationToken ct = default)
    {
        var openPositions = await positions.GetOpenPositionsAsync(ct);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var snapshots = openPositions.Select(position =>
        {
            var order = LivePositionOrderStatusPolicy.Evaluate(position, observedAt);
            return new OpenPositionSnapshot(
                position.Id,
                position.Symbol,
                position.Sector,
                position.Quantity,
                position.EntryPrice,
                position.CurrentPrice,
                position.StopLossPrice,
                position.TargetPrice,
                position.PatternType.ToString(),
                position.UnrealizedPnL,
                position.AccountId,
                position.HighSinceEntry,
                position.EntryAtr,
                Math.Max(0, (observedAt - position.OpenedAt).Days),
                position.OpenedAt,
                order.State.ToString(),
                order.RequestedAt,
                order.Reason,
                order.Kind?.ToString(),
                order.HasBrokerOrderId,
                order.PendingSeconds,
                order.RequestedQuantity,
                order.MarksPartialProfit);
        }).ToArray();

        return new OpenPositionListSnapshot(
            snapshots,
            openPositions.Sum(position => position.UnrealizedPnL),
            observedAt);
    }
}

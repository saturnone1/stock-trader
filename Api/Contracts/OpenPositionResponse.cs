using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Api.Contracts;

public sealed record OpenPositionResponse(
    long Id,
    string Symbol,
    string Sector,
    int Quantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    string Pattern,
    decimal UnrealizedPnL,
    int AccountId,
    decimal HighSinceEntry,
    decimal EntryAtr,
    int HoldingDays,
    string OpenedAt,
    string OrderStatus,
    string? OrderRequestedAt,
    string? OrderReason,
    string? OrderKind,
    bool HasBrokerOrderId,
    long OrderPendingSeconds,
    int OrderQuantity,
    bool OrderMarksPartialProfit);

public static class OpenPositionResponseMapper
{
    public static OpenPositionResponse Map(Position position, DateTime utcNow)
    {
        var order = LivePositionOrderStatusPolicy.Evaluate(position, utcNow);
        return new OpenPositionResponse(
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
            Math.Max(0, (utcNow - position.OpenedAt).Days),
            position.OpenedAt.ToString("o"),
            order.State.ToString(),
            order.RequestedAt?.ToString("o"),
            order.Reason,
            order.Kind?.ToString(),
            order.HasBrokerOrderId,
            order.PendingSeconds,
            order.RequestedQuantity,
            order.MarksPartialProfit);
    }
}

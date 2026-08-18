using StockTrader.Application.Portfolio;

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
    decimal UnrealizedPnLPercent,
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

public sealed record PortfolioHoldingsResponse(
    IReadOnlyList<OpenPositionResponse> Positions,
    decimal TotalUnrealizedPnL,
    int PositionCount);

public sealed record OpenPositionsResponse(
    int Count,
    IReadOnlyList<OpenPositionResponse> Positions);

public static class OpenPositionResponseMapper
{
    public static OpenPositionResponse Map(OpenPositionSnapshot position) => new(
        position.Id,
        position.Symbol,
        position.Sector,
        position.Quantity,
        position.EntryPrice,
        position.CurrentPrice,
        position.StopLossPrice,
        position.TargetPrice,
        position.Pattern,
        position.UnrealizedPnL,
        PositionReturnPolicy.Calculate(
            position.EntryPrice,
            position.Quantity,
            position.UnrealizedPnL),
        position.AccountId,
        position.HighSinceEntry,
        position.EntryAtr,
        position.HoldingDays,
        position.OpenedAt.ToString("o"),
        position.OrderStatus,
        position.OrderRequestedAt?.ToString("o"),
        position.OrderReason,
        position.OrderKind,
        position.HasBrokerOrderId,
        position.OrderPendingSeconds,
        position.OrderQuantity,
        position.OrderMarksPartialProfit);
}

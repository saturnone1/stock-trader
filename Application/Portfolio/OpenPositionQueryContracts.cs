namespace StockTrader.Application.Portfolio;

public sealed record OpenPositionSnapshot(
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
    DateTime OpenedAt,
    string OrderStatus,
    DateTime? OrderRequestedAt,
    string? OrderReason,
    string? OrderKind,
    bool HasBrokerOrderId,
    long OrderPendingSeconds,
    int OrderQuantity,
    bool OrderMarksPartialProfit);

public sealed record OpenPositionListSnapshot(
    IReadOnlyList<OpenPositionSnapshot> Positions,
    decimal TotalUnrealizedPnL,
    DateTime ObservedAt)
{
    public int Count => Positions.Count;
}

public interface IOpenPositionQuery
{
    Task<OpenPositionListSnapshot> GetAsync(CancellationToken ct = default);
}

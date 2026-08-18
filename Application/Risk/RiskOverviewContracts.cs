namespace StockTrader.Application.Risk;

public sealed record RiskStateSnapshot(
    decimal DailyPnL,
    decimal DailyPnLPercent,
    bool IsTradingHalted,
    int OpenPositionCount,
    IReadOnlyDictionary<string, int> PositionsPerSector,
    DateTime LastUpdated);

public sealed record RiskSettingsSnapshot(
    decimal AccountSize,
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector,
    decimal MinExpectancy,
    decimal MinConfidence);

public sealed record PositionRiskSnapshot(
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLossPrice,
    decimal RiskPerShare,
    decimal RMultiple,
    decimal UnrealizedPnL,
    int HoldingDays);

public sealed record RiskOverviewSnapshot(
    RiskStateSnapshot RiskState,
    RiskSettingsSnapshot Settings,
    IReadOnlyList<PositionRiskSnapshot> PositionRMultiples,
    decimal TotalUnrealizedPnL);

public interface IRiskOverviewQuery
{
    Task<RiskOverviewSnapshot> GetAsync(CancellationToken ct = default);
}

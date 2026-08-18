using StockTrader.Application.Risk;

namespace StockTrader.Api.Contracts;

public sealed record RiskStateResponse(
    decimal DailyPnL,
    decimal DailyPnLPercent,
    bool IsTradingHalted,
    int OpenPositionCount,
    IReadOnlyDictionary<string, int> PositionsPerSector,
    string LastUpdated);

public sealed record RiskSettingsResponse(
    decimal AccountSize,
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector,
    decimal MinExpectancy,
    decimal MinConfidence);

public sealed record PositionRiskResponse(
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLossPrice,
    decimal RiskPerShare,
    decimal RMultiple,
    decimal UnrealizedPnL,
    int HoldingDays);

public sealed record RiskOverviewResponse(
    RiskStateResponse RiskState,
    RiskSettingsResponse Settings,
    IReadOnlyList<PositionRiskResponse> PositionRMultiples,
    decimal TotalUnrealizedPnL)
{
    public static RiskOverviewResponse Create(RiskOverviewSnapshot snapshot) => new(
        new RiskStateResponse(
            snapshot.RiskState.DailyPnL,
            snapshot.RiskState.DailyPnLPercent,
            snapshot.RiskState.IsTradingHalted,
            snapshot.RiskState.OpenPositionCount,
            snapshot.RiskState.PositionsPerSector,
            snapshot.RiskState.LastUpdated.ToString("o")),
        new RiskSettingsResponse(
            snapshot.Settings.AccountSize,
            snapshot.Settings.RiskPerTradePercent,
            snapshot.Settings.DailyLossLimitPercent,
            snapshot.Settings.MaxTotalPositions,
            snapshot.Settings.MaxPositionsPerSector,
            snapshot.Settings.MinExpectancy,
            snapshot.Settings.MinConfidence),
        snapshot.PositionRMultiples.Select(position => new PositionRiskResponse(
            position.Symbol,
            position.Pattern,
            position.EntryPrice,
            position.CurrentPrice,
            position.StopLossPrice,
            position.RiskPerShare,
            position.RMultiple,
            position.UnrealizedPnL,
            position.HoldingDays)).ToArray(),
        snapshot.TotalUnrealizedPnL);
}

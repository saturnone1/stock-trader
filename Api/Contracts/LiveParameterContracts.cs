using StockTrader.Models;

namespace StockTrader.Api.Contracts;

public sealed record ApplyLiveRequest(
    PatternParameterOverrides? ParameterOverrides,
    IReadOnlyList<PatternType> EnabledPatterns,
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

public sealed record ApplyLiveResponse(string Message, DateTime LastModified);

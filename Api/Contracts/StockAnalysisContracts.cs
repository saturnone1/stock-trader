using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Api.Contracts;

public sealed record StockAnalysisIndicatorResponse(
    decimal Rsi,
    decimal Sma20,
    decimal Sma50,
    decimal Sma200,
    decimal Macd,
    decimal MacdSignal,
    decimal BollingerUpper,
    decimal BollingerMiddle,
    decimal BollingerLower,
    decimal Vwap,
    int BullishIndicatorCount,
    int TotalIndicatorCount)
{
    public static StockAnalysisIndicatorResponse Create(IndicatorSnapshot value) => new(
        value.RSI,
        value.SMA20,
        value.SMA50,
        value.SMA200,
        value.MACD,
        value.MACDSignal,
        value.BollingerUpper,
        value.BollingerMiddle,
        value.BollingerLower,
        value.VWAP,
        value.BullishIndicatorCount,
        value.TotalIndicatorCount);
}

public sealed record StockAnalysisPatternResponse(
    string Pattern,
    string PatternName,
    decimal Confidence,
    decimal HistoricalWinRate,
    decimal HistoricalAvgReturn)
{
    public static StockAnalysisPatternResponse Create(PatternSignalInfo value) => new(
        value.PatternType.ToString(),
        PatternCatalog.DisplayName(value.PatternType),
        value.Confidence,
        value.HistoricalWinRate,
        value.HistoricalAvgReturn);
}

public sealed record StockAnalysisResponse(
    string Symbol,
    decimal CurrentPrice,
    string Grade,
    decimal UpsideProbability,
    decimal ExpectedReturnPercent,
    int ExpectedHoldingDays,
    decimal DownsideRiskPercent,
    decimal RecommendedStopLoss,
    decimal RecommendedTarget,
    decimal ConfidenceScore,
    decimal Atr,
    StockAnalysisIndicatorResponse Indicators,
    IReadOnlyList<StockAnalysisPatternResponse> ActivePatterns,
    DateTime AnalyzedAt)
{
    public static StockAnalysisResponse Create(StockAnalysis value) => new(
        value.Symbol,
        value.CurrentPrice,
        value.Grade.ToString(),
        value.UpsideProbability,
        value.ExpectedReturnPercent,
        value.ExpectedHoldingDays,
        value.DownsideRiskPercent,
        value.RecommendedStopLoss,
        value.RecommendedTarget,
        value.ConfidenceScore,
        value.ATR,
        StockAnalysisIndicatorResponse.Create(value.Indicators),
        value.ActivePatterns.Select(StockAnalysisPatternResponse.Create).ToArray(),
        value.AnalyzedAt);
}

public sealed record StockAnalysisErrorResponse(string Error);

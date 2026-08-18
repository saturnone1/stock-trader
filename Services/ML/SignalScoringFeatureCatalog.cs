using StockTrader.Application.MachineLearning;

namespace StockTrader.Services.ML;

internal sealed record SignalScoringFeatureDescriptor(
    string ColumnName,
    string DisplayName,
    Func<SignalScorerInput, float> Read,
    Action<SignalScorerInput, float> Write);

/// <summary>시그널 스코어러 피처 순서, 이름과 영속 스키마 버전의 단일 소유자입니다.</summary>
internal static class SignalScoringFeatureCatalog
{
    public static readonly IReadOnlyList<SignalScoringFeatureDescriptor> All =
    [
        new(nameof(SignalScorerInput.PatternTypeCode), "패턴 유형",
            input => input.PatternTypeCode, (input, value) => input.PatternTypeCode = value),
        new(nameof(SignalScorerInput.Rsi), "RSI",
            input => input.Rsi, (input, value) => input.Rsi = value),
        new(nameof(SignalScorerInput.BollingerPosition), "볼린저 위치",
            input => input.BollingerPosition, (input, value) => input.BollingerPosition = value),
        new(nameof(SignalScorerInput.VolumeRatio), "거래량 비율",
            input => input.VolumeRatio, (input, value) => input.VolumeRatio = value),
        new(nameof(SignalScorerInput.MarketRegimeCode), "시장 레짐",
            input => input.MarketRegimeCode, (input, value) => input.MarketRegimeCode = value),
        new(nameof(SignalScorerInput.AtrPercent), "ATR%",
            input => input.AtrPercent, (input, value) => input.AtrPercent = value),
        new(nameof(SignalScorerInput.HistoricalWinRate), "역사적 승률",
            input => input.HistoricalWinRate, (input, value) => input.HistoricalWinRate = value),
        new(nameof(SignalScorerInput.RiskRewardRatio), "계획 손익비",
            input => input.RiskRewardRatio, (input, value) => input.RiskRewardRatio = value),
        new(nameof(SignalScorerInput.PriceVsLongMovingAverage), "장기 이동평균 대비 위치",
            input => input.PriceVsLongMovingAverage,
            (input, value) => input.PriceVsLongMovingAverage = value),
        new(nameof(SignalScorerInput.LongTrendHistoryAvailable), "장기 추세 이력 보유",
            input => input.LongTrendHistoryAvailable,
            (input, value) => input.LongTrendHistoryAvailable = value),
    ];

    public static string[] ColumnNames => All.Select(feature => feature.ColumnName).ToArray();

    static SignalScoringFeatureCatalog()
    {
        if (All.Count != SignalScoringFeatureSchema.FeatureCount)
        {
            throw new InvalidOperationException(
                "Signal scoring feature catalog count does not match its persisted schema.");
        }
    }

    public static SignalScorerInput ToModelInput(
        SignalScoringFeatures features,
        bool label = false) => new()
    {
        Label = label,
        PatternTypeCode = features.PatternTypeCode,
        Rsi = features.Rsi,
        BollingerPosition = features.BollingerPosition,
        VolumeRatio = features.VolumeRatio,
        MarketRegimeCode = features.MarketRegimeCode,
        AtrPercent = features.AtrPercent,
        HistoricalWinRate = features.HistoricalWinRate,
        RiskRewardRatio = features.RiskRewardRatio,
        PriceVsLongMovingAverage = features.PriceVsLongMovingAverage,
        LongTrendHistoryAvailable = features.LongTrendHistoryAvailable,
    };

    public static SignalScorerInput Clone(SignalScorerInput source) => new()
    {
        Label = source.Label,
        PatternTypeCode = source.PatternTypeCode,
        Rsi = source.Rsi,
        BollingerPosition = source.BollingerPosition,
        VolumeRatio = source.VolumeRatio,
        MarketRegimeCode = source.MarketRegimeCode,
        AtrPercent = source.AtrPercent,
        HistoricalWinRate = source.HistoricalWinRate,
        RiskRewardRatio = source.RiskRewardRatio,
        PriceVsLongMovingAverage = source.PriceVsLongMovingAverage,
        LongTrendHistoryAvailable = source.LongTrendHistoryAvailable,
    };
}

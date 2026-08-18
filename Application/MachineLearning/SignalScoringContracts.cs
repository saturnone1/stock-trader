using StockTrader.Models;

namespace StockTrader.Application.MachineLearning;

public static class SignalScoringFeatureSchema
{
    public const int CurrentVersion = 1;
    public const int FeatureCount = 10;
}

/// <summary>
/// 진입 판단 시점에 확정된 인과적 ML 피처입니다. 청산 이후 정보는 포함할 수 없습니다.
/// </summary>
public sealed record SignalScoringFeatures(
    int SchemaVersion,
    float PatternTypeCode,
    float Rsi,
    float BollingerPosition,
    float VolumeRatio,
    float MarketRegimeCode,
    float AtrPercent,
    float HistoricalWinRate,
    float RiskRewardRatio,
    float PriceVsLongMovingAverage,
    float LongTrendHistoryAvailable);

public sealed record SignalScoringResult(
    decimal Confidence,
    SignalScoringFeatures? Features);

/// <summary>하나의 원래 진입 판단과 그 포지션에서 실현된 최종 손익 레이블입니다.</summary>
public sealed record SignalScoringTrainingSample(
    long SourceSignalId,
    DateTime SignalBarAt,
    SignalScoringFeatures Features,
    bool IsWin);

public interface ISignalScoringTrainingStore
{
    Task<IReadOnlyList<SignalScoringTrainingSample>> GetRecentAsync(
        int limit,
        CancellationToken ct = default);
}

public sealed record FeatureImportance(
    string FeatureName,
    double Importance);

public sealed record SignalScorerStatus(
    bool IsModelLoaded,
    DateTime? TrainedAt,
    int TrainingSamples,
    double ValidationAccuracy,
    double ValidationAuc,
    IReadOnlyList<FeatureImportance> FeatureImportances);

public interface ISignalScorer
{
    Task<SignalScoringResult> EvaluateAsync(
        PatternSignal signal,
        OhlcvBar[] bars,
        MarketRegime regime,
        decimal historicalWinRate = 0.5m,
        CancellationToken ct = default);

    Task<bool> TrainAsync(
        IReadOnlyList<SignalScoringTrainingSample> samples,
        CancellationToken ct = default);

    bool IsModelLoaded { get; }
    SignalScorerStatus GetStatus();
}

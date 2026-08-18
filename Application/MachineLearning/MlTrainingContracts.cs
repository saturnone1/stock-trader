using StockTrader.Models;

namespace StockTrader.Application.MachineLearning;

public sealed record MlTrainingOptions(
    int MinimumTrainingSamples,
    int RegimeTrainingDays,
    int SignalSampleLimit);

public sealed record MarketRegimeTrainingSet(
    string Symbol,
    IReadOnlyList<OhlcvBar> Bars);

public interface IMarketRegimeTrainingDataSource
{
    Task<MarketRegimeTrainingSet> LoadAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}

/// <summary>운영자가 조회하는 두 ML 모델의 검증된 현재 상태입니다.</summary>
public sealed record MlModelStatus(
    bool IsRegimeModelLoaded,
    DateTime? RegimeModelTrainedAt,
    int RegimeTrainingSamples,
    IReadOnlyDictionary<uint, string> RegimeClusterLabels,
    bool IsSignalScorerLoaded,
    DateTime? SignalScorerTrainedAt,
    int SignalScorerTrainingSamples,
    double SignalScorerAccuracy,
    double SignalScorerAuc,
    IReadOnlyList<FeatureImportance> SignalScorerFeatureImportances,
    bool IsTraining,
    string TrainingStatus);

public sealed class MlTrainingResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int RegimeSamples { get; init; }
    public int SignalSamples { get; init; }
    public double SignalScorerAccuracy { get; init; }
    public double SignalScorerAuc { get; init; }
    public TimeSpan TrainingDuration { get; init; }
}

public interface IMLModelTrainingService
{
    Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default);
}

public interface IMlModelStatusQuery
{
    MlModelStatus GetStatus();
}

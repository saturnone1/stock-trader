using StockTrader.Models;

namespace StockTrader.Application.MachineLearning;

public sealed record MlTrainingOptions(
    int MinimumTrainingSamples,
    int RegimeTrainingDays,
    int SignalSampleLimit);

public sealed record MarketRegimeTrainingSet(
    string Symbol,
    string Provider,
    string TimeFrame,
    string AdjustmentMode,
    string CalendarVersion,
    string ContentHash,
    string EvidenceId,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<OhlcvBar> Bars)
{
    public MarketRegimeTrainingSet(string symbol, IReadOnlyList<OhlcvBar> bars)
        : this(symbol, "Compatibility", "Daily", "Raw", "compatibility-v1",
            StockTrader.ServiceContracts.MachineLearning.MlTrainingContractHash.Sha256(symbol),
            StockTrader.ServiceContracts.MachineLearning.MlTrainingContractHash.Sha256($"evidence|{symbol}"),
            bars.Count == 0 ? DateTime.UnixEpoch : bars.Min(bar => bar.Timestamp),
            bars.Count == 0 ? DateTime.UnixEpoch : bars.Max(bar => bar.Timestamp), bars)
    {
    }
}

public interface IMlTrainingTransport
{
    Task<StockTrader.ServiceContracts.MachineLearning.MlTrainingJobResult> TrainAsync(
        MarketRegimeTrainingSet regime,
        IReadOnlyList<SignalScoringTrainingSample> signalSamples,
        MlTrainingOptions options,
        DateTime requestedAtUtc,
        CancellationToken ct = default);
}

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

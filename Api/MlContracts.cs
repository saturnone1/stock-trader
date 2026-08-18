namespace StockTrader.Api;

public sealed record MlFeatureImportanceResponse(
    string FeatureName,
    double Importance);

public sealed record MlRegimeClassifierStatusResponse(
    bool IsRegimeModelLoaded,
    string? TrainedAt,
    int RegimeTrainingSamples,
    IReadOnlyDictionary<string, string> ClusterLabels);

public sealed record MlSignalScorerStatusResponse(
    bool IsSignalScorerLoaded,
    string? TrainedAt,
    int SignalScorerTrainingSamples,
    double SignalScorerAccuracy,
    double SignalScorerAuc,
    IReadOnlyList<MlFeatureImportanceResponse> FeatureImportances);

public sealed record MlStatusResponse(
    MlRegimeClassifierStatusResponse RegimeClassifier,
    MlSignalScorerStatusResponse SignalScorer,
    bool IsTraining,
    string TrainingStatus);

public sealed record MlTrainingResponse(
    bool Success,
    string Message,
    int RegimeSamples,
    int SignalSamples,
    double SignalScorerAccuracy,
    double SignalScorerAuc,
    double TrainingDurationSeconds);

public sealed record MlTrainingErrorResponse(
    bool Success,
    string Message);

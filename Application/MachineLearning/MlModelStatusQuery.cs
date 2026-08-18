namespace StockTrader.Application.MachineLearning;

/// <summary>두 실행 모델과 전역 학습 claim을 하나의 운영 상태로 투영합니다.</summary>
internal sealed class MlModelStatusQuery(
    IMarketRegimeClassifier regimeClassifier,
    ISignalScorer signalScorer,
    MlTrainingRunState runState) : IMlModelStatusQuery
{
    public MlModelStatus GetStatus()
    {
        var regime = regimeClassifier.GetStatus();
        var scorer = signalScorer.GetStatus();
        var run = runState.Snapshot();
        return new MlModelStatus(
            regime.IsModelLoaded,
            regime.TrainedAt,
            regime.TrainingSamples,
            regime.ClusterLabels,
            scorer.IsModelLoaded,
            scorer.TrainedAt,
            scorer.TrainingSamples,
            scorer.ValidationAccuracy,
            scorer.ValidationAuc,
            scorer.FeatureImportances,
            run.IsTraining,
            run.Status);
    }
}

using StockTrader.Services.ML;

namespace StockTrader.Api;

public static class MlEndpoints
{
    public static RouteGroupBuilder MapMlApi(this RouteGroupBuilder group)
    {
        // GET /api/ml — ML 모델 상태
        group.MapGet("/ml", (
            IMLModelTrainingService trainingService,
            IMarketRegimeClassifier regimeClassifier,
            ISignalScorer signalScorer) =>
        {
            var status = trainingService.GetStatus();

            return Results.Ok(new
            {
                RegimeClassifier = new
                {
                    status.IsRegimeModelLoaded,
                    TrainedAt       = status.RegimeModelTrainedAt?.ToString("o"),
                    status.RegimeTrainingSamples,
                    ClusterLabels   = regimeClassifier.ClusterLabels
                        .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
                },
                SignalScorer = new
                {
                    status.IsSignalScorerLoaded,
                    TrainedAt           = status.SignalScorerTrainedAt?.ToString("o"),
                    status.SignalScorerTrainingSamples,
                    status.SignalScorerAccuracy,
                    SignalScorerAuc     = signalScorer.LastAuc,
                    FeatureImportances  = signalScorer.FeatureImportances.Select(f => new
                    {
                        f.FeatureName,
                        f.Importance
                    })
                },
                IsTraining      = status.IsTraining,
                TrainingStatus  = status.TrainingStatus
            });
        }).RequireAuthorization();

        // POST /api/ml/train — 모델 학습 트리거
        group.MapPost("/ml/train", async (
            IMLModelTrainingService trainingService,
            CancellationToken ct) =>
        {
            var result = await trainingService.TrainAllAsync(ct);

            return result.Success
                ? Results.Ok(new
                {
                    result.Success,
                    result.Message,
                    result.RegimeSamples,
                    result.SignalSamples,
                    result.SignalScorerAccuracy,
                    result.SignalScorerAuc,
                    TrainingDurationSeconds = result.TrainingDuration.TotalSeconds
                })
                : Results.BadRequest(new
                {
                    result.Success,
                    result.Message
                });
        }).RequireAuthorization();

        return group;
    }
}

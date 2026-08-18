using StockTrader.Application.MachineLearning;

namespace StockTrader.Api;

public static class MlEndpoints
{
    public static RouteGroupBuilder MapMlApi(this RouteGroupBuilder group)
    {
        group.MapGet("/ml", (IMlModelStatusQuery statusQuery) =>
        {
            var status = statusQuery.GetStatus();
            return TypedResults.Ok(new MlStatusResponse(
                new MlRegimeClassifierStatusResponse(
                    status.IsRegimeModelLoaded,
                    status.RegimeModelTrainedAt?.ToString("o"),
                    status.RegimeTrainingSamples,
                    status.RegimeClusterLabels.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => pair.Value)),
                new MlSignalScorerStatusResponse(
                    status.IsSignalScorerLoaded,
                    status.SignalScorerTrainedAt?.ToString("o"),
                    status.SignalScorerTrainingSamples,
                    status.SignalScorerAccuracy,
                    status.SignalScorerAuc,
                    status.SignalScorerFeatureImportances
                        .Select(feature => new MlFeatureImportanceResponse(
                            feature.FeatureName,
                            feature.Importance))
                        .ToArray()),
                status.IsTraining,
                status.TrainingStatus));
        }).RequireAuthorization();

        group.MapPost("/ml/train", async (
            IMLModelTrainingService training,
            CancellationToken ct) =>
        {
            var result = await training.TrainAllAsync(ct);
            return result.Success
                ? Results.Ok(new MlTrainingResponse(
                    result.Success,
                    result.Message,
                    result.RegimeSamples,
                    result.SignalSamples,
                    result.SignalScorerAccuracy,
                    result.SignalScorerAuc,
                    result.TrainingDuration.TotalSeconds))
                : Results.BadRequest(new MlTrainingErrorResponse(
                    result.Success,
                    result.Message));
        }).RequireAuthorization()
          .Produces<MlTrainingResponse>()
          .Produces<MlTrainingErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }
}

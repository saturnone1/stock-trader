using Microsoft.Extensions.Options;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>
/// Restores the latest service-owned publication into the API's verified inference cache.
/// Training remains exclusively owned by the remote service.
/// </summary>
internal sealed class MlTrainingPublicationReconciliationService(
    IOptions<MlTrainingTransportOptions> options,
    IMlTrainingTransport transport,
    IMarketRegimeClassifier regimeClassifier,
    ISignalScorer signalScorer,
    ILogger<MlTrainingPublicationReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode != "Remote") return;

        try
        {
            var snapshot = await transport.GetLatestPublicationAsync(stoppingToken);
            if (snapshot is null || snapshot.PublicationRevision == 0) return;

            var regimeImported = ImportIfCurrent(
                snapshot.RegimeArtifact,
                regimeClassifier.GetStatus().TrainedAt,
                regimeClassifier.ImportArtifact);
            var signalImported = ImportIfCurrent(
                snapshot.SignalArtifact,
                signalScorer.GetStatus().TrainedAt,
                signalScorer.ImportArtifact);

            logger.LogInformation(
                "ML Training publication revision {Revision} reconciled: regime={Regime}, signal={Signal}",
                snapshot.PublicationRevision, regimeImported, signalImported);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "ML Training publication reconciliation failed; verified cache remains unchanged");
        }
    }

    private static string ImportIfCurrent(
        StockTrader.ServiceContracts.MachineLearning.MlModelArtifactContract? artifact,
        DateTime? cachedTrainedAt,
        Func<StockTrader.ServiceContracts.MachineLearning.MlModelArtifactContract, bool> import)
    {
        if (artifact is null) return "absent";
        if (cachedTrainedAt.HasValue && cachedTrainedAt.Value > artifact.TrainedAtUtc)
            return "stale-skipped";
        if (!import(artifact))
            throw new InvalidOperationException($"ml-training-artifact-import-failed:{artifact.ModelKind}");
        return "imported";
    }
}

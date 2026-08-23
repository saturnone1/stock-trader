using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.MlTrainingCompute;
using StockTrader.ServiceContracts.MachineLearning;

namespace StockTrader.Services.ML;

internal sealed class MlTrainingTransport : IMlTrainingTransport, IDisposable
{
    private readonly MlTrainingTransportOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MlTrainingTransport> _logger;
    private readonly HttpClient? _client;

    public MlTrainingTransport(IOptions<MlTrainingTransportOptions> options,
        TimeProvider clock, ILogger<MlTrainingTransport> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        if (_options.Mode != "Local") _client = CreateClient(_options);
    }

    public async Task<MlTrainingJobResult> TrainAsync(
        MarketRegimeTrainingSet regime,
        IReadOnlyList<SignalScoringTrainingSample> signalSamples,
        MlTrainingOptions settings,
        DateTime requestedAtUtc,
        CancellationToken ct = default)
    {
        var request = CreateRequest(regime, signalSamples, settings, requestedAtUtc);
        if (_options.Mode == "Local") return Local(request, ct);
        if (_options.Mode == "Remote") return await Remote(request, ct);

        var local = Local(request, ct);
        try
        {
            var remote = await Remote(request, ct);
            var mismatch = ParityError(request, local, remote);
            if (mismatch is not null)
                _logger.LogError("ML Training Shadow mismatch for {JobId}: {Mismatch}", request.JobId, mismatch);
            else
                _logger.LogInformation("ML Training Shadow parity confirmed for {JobId}", request.JobId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "ML Training Shadow service unavailable; Local remains authoritative");
        }
        return local;
    }

    private MlTrainingJobRequest CreateRequest(MarketRegimeTrainingSet regime,
        IReadOnlyList<SignalScoringTrainingSample> signals, MlTrainingOptions settings,
        DateTime requestedAtUtc)
    {
        var observed = requestedAtUtc.Kind == DateTimeKind.Utc
            ? requestedAtUtc : requestedAtUtc.ToUniversalTime();
        var evidence = new MlRegimeDataEvidenceContract(
            regime.Provider, regime.Symbol, regime.TimeFrame, regime.AdjustmentMode,
            regime.CalendarVersion, regime.FromUtc, regime.ToUtc,
            regime.ContentHash, regime.EvidenceId);
        var features = MarketRegimeFeatureFactory.CreateTrainingSamples(regime.Bars, observed)
            .Select(x => new MlRegimeFeatureContract(x.AsOfUtc, x.Return5Day,
                x.Return10Day, x.Return20Day, x.VolatilityLevel,
                x.VolumeChangeRate, x.MaSlopePercent, x.Rsi)).ToArray();
        var signal = signals.OrderBy(x => x.SignalBarAt).ThenBy(x => x.SourceSignalId)
            .Select(x => new MlSignalTrainingSampleContract(
                x.SourceSignalId, x.SignalBarAt,
                new MlSignalFeatureContract(x.Features.SchemaVersion,
                    x.Features.PatternTypeCode, x.Features.Rsi,
                    x.Features.BollingerPosition, x.Features.VolumeRatio,
                    x.Features.MarketRegimeCode, x.Features.AtrPercent,
                    x.Features.HistoricalWinRate, x.Features.RiskRewardRatio,
                    x.Features.PriceVsLongMovingAverage,
                    x.Features.LongTrendHistoryAvailable), x.IsWin)).ToArray();
        var draft = new MlTrainingJobRequest(
            MlTrainingContractVersions.Current, "pending", string.Empty,
            MlTrainingContractVersions.Trainer,
            MlTrainingContractVersions.RegimeFeatureSchema,
            MlTrainingContractVersions.SignalFeatureSchema, observed, observed,
            settings.MinimumTrainingSamples,
            MlTrainingContractVersions.RequiredRegimeClusters,
            evidence, features, signal);
        var hash = MlTrainingContractHash.Input(draft);
        return draft with { JobId = $"ml-{hash[..32]}", InputHash = hash };
    }

    private MlTrainingJobResult Local(MlTrainingJobRequest request, CancellationToken ct)
    {
        var started = _clock.GetUtcNow().UtcDateTime;
        var compute = MlTrainingComputeFacade.Train(request, ct);
        var completed = _clock.GetUtcNow().UtcDateTime;
        return new MlTrainingJobResult(MlTrainingContractVersions.Current,
            request.JobId, request.InputHash, compute.Status, compute.Message, 0,
            started, started, completed,
            (long)(completed - started).TotalMilliseconds,
            compute.RegimeArtifact, compute.SignalArtifact, false);
    }

    private async Task<MlTrainingJobResult> Remote(MlTrainingJobRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var client = _client ?? throw new InvalidOperationException("ml-training-client-not-configured");
        using var response = await client.PostAsJsonAsync("/v1/training/jobs", request, timeout.Token);
        response.EnsureSuccessStatusCode();
        while (true)
        {
            await Task.Delay(_options.PollMilliseconds, timeout.Token);
            using var poll = await client.GetAsync($"/v1/training/jobs/{request.JobId}", timeout.Token);
            poll.EnsureSuccessStatusCode();
            var payload = await poll.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(payload);
            var status = document.RootElement.GetProperty("status").GetString();
            if (status is MlTrainingJobStatuses.Pending or MlTrainingJobStatuses.Running) continue;
            if (status == MlTrainingJobStatuses.Failed)
            {
                var error = document.RootElement.TryGetProperty("error", out var value)
                    ? value.GetString() : "remote-training-failed";
                throw new InvalidOperationException(error);
            }
            if (status == MlTrainingJobStatuses.Cancelled
                && !document.RootElement.TryGetProperty("contractVersion", out _))
                throw new OperationCanceledException("remote-training-cancelled");
            var result = JsonSerializer.Deserialize<MlTrainingJobResult>(payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("empty-ml-training-result");
            ValidateResult(request, result);
            return result;
        }
    }

    private static void ValidateResult(MlTrainingJobRequest request, MlTrainingJobResult result)
    {
        if (result.JobId != request.JobId || result.InputHash != request.InputHash)
            throw new InvalidOperationException("ml-training-result-identity-mismatch");
        foreach (var artifact in new[] { result.RegimeArtifact, result.SignalArtifact })
        {
            if (artifact is null) continue;
            var error = MlTrainingContractPolicy.ArtifactError(artifact);
            if (error is not null) throw new InvalidOperationException(error);
            if (artifact.TrainingCutoffUtc != request.ObservationCutoffUtc)
                throw new InvalidOperationException("ml-training-artifact-cutoff-mismatch");
        }
    }

    private static string? ParityError(
        MlTrainingJobRequest request, MlTrainingJobResult local, MlTrainingJobResult remote)
    {
        if (local.Status != remote.Status) return "status";
        if (!Equivalent(local.RegimeArtifact, remote.RegimeArtifact)) return "regime-manifest";
        if (!Equivalent(local.SignalArtifact, remote.SignalArtifact)) return "signal-manifest";
        if (local.RegimeArtifact is not null && remote.RegimeArtifact is not null)
        {
            foreach (var probe in request.RegimeSamples.Take(3).Append(request.RegimeSamples[^1]))
                if (MlTrainingComputeFacade.PredictRegime(local.RegimeArtifact.ModelBytes, probe)
                    != MlTrainingComputeFacade.PredictRegime(remote.RegimeArtifact.ModelBytes, probe))
                    return "regime-prediction";
        }
        if (local.SignalArtifact is not null && remote.SignalArtifact is not null)
        {
            foreach (var probe in request.SignalSamples.Take(3).Select(x => x.Features))
                if (Math.Abs(MlTrainingComputeFacade.PredictSignal(local.SignalArtifact.ModelBytes, probe)
                    - MlTrainingComputeFacade.PredictSignal(remote.SignalArtifact.ModelBytes, probe)) > 0.000001f)
                    return "signal-prediction";
        }
        return null;
    }

    private static bool Equivalent(MlModelArtifactContract? left, MlModelArtifactContract? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.ModelKind == right.ModelKind
            && left.TrainerVersion == right.TrainerVersion
            && left.FeatureSchemaVersion == right.FeatureSchemaVersion
            && left.FeatureCount == right.FeatureCount
            && left.TrainingCutoffUtc == right.TrainingCutoffUtc
            && left.TrainingSamples == right.TrainingSamples
            && left.ValidationAccuracy == right.ValidationAccuracy
            && left.ValidationAuc == right.ValidationAuc
            && (left.ClusterLabels ?? new Dictionary<uint, string>())
                .OrderBy(x => x.Key).SequenceEqual(
                    (right.ClusterLabels ?? new Dictionary<uint, string>()).OrderBy(x => x.Key))
            && left.FeatureImportances.SequenceEqual(right.FeatureImportances);
    }

    private static HttpClient CreateClient(MlTrainingTransportOptions options)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(X509Certificate2.CreateFromPemFile(
            options.ClientCertificatePath, options.ClientCertificateKeyPath));
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(
                X509Certificate2.CreateFromPemFile(options.ServerCertificateAuthorityPath));
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate)
                && certificate.GetNameInfo(X509NameType.DnsName, false)
                    .Equals(options.ServerCertificateCommonName, StringComparison.Ordinal);
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.Endpoint) };
        client.DefaultRequestHeaders.Add("X-StockTrader-Worker-Secret", options.SharedSecret);
        return client;
    }

    public void Dispose() => _client?.Dispose();
}

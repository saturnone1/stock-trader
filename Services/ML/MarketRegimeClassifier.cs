using Microsoft.Extensions.Options;
using Microsoft.ML;
using StockTrader.Application.MachineLearning;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.ML;

/// <summary>인과적 레짐 피처, K-Means 학습과 검증된 모델 교체를 조율합니다.</summary>
public sealed class MarketRegimeClassifier : IMarketRegimeClassifier
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly MLSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly MarketRegimeModelTrainer _trainer;
    private readonly MarketRegimeModelArtifactStore _artifacts;
    private readonly ILogger<MarketRegimeClassifier> _logger;
    private readonly object _modelLock = new();

    private ITransformer? _model;
    private PredictionEngine<RegimeFeatureInput, RegimeClusterOutput>? _predictionEngine;
    private IReadOnlyDictionary<uint, string> _clusterLabels =
        new Dictionary<uint, string>();

    public bool IsModelLoaded
    {
        get
        {
            lock (_modelLock) return _model is not null;
        }
    }

    private DateTime? _trainedAt;
    private int _trainingSamples;

    public MarketRegimeClassifierStatus GetStatus()
    {
        lock (_modelLock)
        {
            return new MarketRegimeClassifierStatus(
                _model is not null,
                _trainedAt,
                _trainingSamples,
                new Dictionary<uint, string>(_clusterLabels));
        }
    }

    public MarketRegimeClassifier(
        IOptions<MLSettings> settings,
        TimeProvider timeProvider,
        ILogger<MarketRegimeClassifier> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _trainer = new MarketRegimeModelTrainer(_mlContext);
        _artifacts = new MarketRegimeModelArtifactStore(
            _mlContext, _settings, logger);
        _logger = logger;
        var stored = _artifacts.TryLoad();
        if (stored is not null) Publish(stored.Model, stored.Manifest);
    }

    public Task<MarketRegime> ClassifyAsync(
        OhlcvBar[] benchmarkBars,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var baseRegime = MarketRegimeTrendPolicy.Evaluate(benchmarkBars, observedAt);
        var features = MarketRegimeFeatureFactory.CreateLatest(benchmarkBars, observedAt);
        if (features is null) return Task.FromResult(baseRegime);

        try
        {
            RegimeClusterOutput prediction;
            string? label;
            lock (_modelLock)
            {
                if (_predictionEngine is null)
                    return Task.FromResult(baseRegime);
                prediction = _predictionEngine.Predict(
                    MarketRegimeFeatureCatalog.ToModelInput(features));
                _clusterLabels.TryGetValue(prediction.ClusterId, out label);
            }
            if (label is null) return Task.FromResult(baseRegime);

            return Task.FromResult(new MarketRegime
            {
                SpyAbove200Ma = baseRegime.SpyAbove200Ma,
                SpyPrice = baseRegime.SpyPrice,
                Spy200Ma = baseRegime.Spy200Ma,
                VixLevel = baseRegime.VixLevel,
                RegimeLabel = label,
                AsOf = baseRegime.AsOf,
                MlClusterId = (int)prediction.ClusterId,
                MlRegimeLabel = label,
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "ML 레짐 분류 실패 — 기본 레짐 사용");
            return Task.FromResult(baseRegime);
        }
    }

    public async Task<bool> TrainAsync(
        OhlcvBar[] benchmarkBars,
        CancellationToken ct = default)
    {
        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var features = await Task.Run(
                () => MarketRegimeFeatureFactory.CreateTrainingSamples(
                    benchmarkBars, observedAt), ct);
            if (features.Count < _settings.MinTrainingSamples)
            {
                _logger.LogWarning(
                    "레짐 모델 인과적 피처 부족: {Count}개 (최소 {Min}개)",
                    features.Count, _settings.MinTrainingSamples);
                return false;
            }

            var fitted = await Task.Run(
                () => _trainer.Fit(features, _settings.RegimeClusterCount), ct);
            var manifest = new MarketRegimeModelManifest(
                MarketRegimeFeatureSchema.CurrentVersion,
                MarketRegimeFeatureSchema.FeatureCount,
                _settings.RegimeClusterCount,
                _timeProvider.GetUtcNow().UtcDateTime,
                features.Count,
                new Dictionary<uint, string>(fitted.ClusterLabels),
                string.Empty);
            if (!_artifacts.TrySave(fitted.Model, manifest, out var savedManifest))
                return false;
            Publish(fitted.Model, savedManifest);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "시장 레짐 분류기 학습 실패");
            return false;
        }
    }

    public bool ImportArtifact(
        StockTrader.ServiceContracts.MachineLearning.MlModelArtifactContract artifact)
    {
        var stored = _artifacts.TryImport(artifact);
        if (stored is null) return false;
        Publish(stored.Model, stored.Manifest);
        return true;
    }

    private void Publish(ITransformer model, MarketRegimeModelManifest manifest)
    {
        var engine = _mlContext.Model
            .CreatePredictionEngine<RegimeFeatureInput, RegimeClusterOutput>(model);
        lock (_modelLock)
        {
            _model = model;
            _predictionEngine = engine;
            _clusterLabels = new Dictionary<uint, string>(manifest.ClusterLabels);
            _trainedAt = manifest.TrainedAtUtc;
            _trainingSamples = manifest.TrainingSamples;
        }
    }
}

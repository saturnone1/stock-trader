using Microsoft.Extensions.Options;
using Microsoft.ML;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.ML;

/// <summary>인과적 피처 생성, 검증된 모델 교체와 신뢰도 예측을 조율합니다.</summary>
public sealed class SignalScorer : ISignalScorer
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly MLSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SignalScoringFeatureFactory _features;
    private readonly SignalScorerModelTrainer _trainer;
    private readonly SignalScorerModelArtifactStore _artifacts;
    private readonly ILogger<SignalScorer> _logger;
    private readonly object _modelLock = new();

    private ITransformer? _model;
    private PredictionEngine<SignalScorerInput, SignalScorerOutput>? _predictionEngine;

    public bool IsModelLoaded
    {
        get
        {
            lock (_modelLock) return _model is not null;
        }
    }
    private DateTime? _trainedAt;
    private int _trainingSamples;
    private double _lastAccuracy;
    private double _lastAuc;
    private IReadOnlyList<FeatureImportance> _featureImportances = [];

    public SignalScorer(
        IOptions<MLSettings> settings,
        TimeProvider timeProvider,
        IIndicatorService indicators,
        ILogger<SignalScorer> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _features = new SignalScoringFeatureFactory(indicators);
        _trainer = new SignalScorerModelTrainer(_mlContext);
        _artifacts = new SignalScorerModelArtifactStore(
            _mlContext, _settings, logger);
        _logger = logger;
        var stored = _artifacts.TryLoad();
        if (stored is not null) Publish(stored.Model, stored.Manifest);
    }

    public Task<SignalScoringResult> EvaluateAsync(
        PatternSignal signal,
        OhlcvBar[] bars,
        MarketRegime regime,
        decimal historicalWinRate = 0.5m,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        SignalScoringFeatures? features = null;
        try
        {
            features = _features.Create(signal, bars, regime, historicalWinRate);
            if (features is null || !_settings.EnableMlScoring)
                return Task.FromResult(new SignalScoringResult(signal.Confidence, features));

            SignalScorerOutput prediction;
            lock (_modelLock)
            {
                if (_predictionEngine is null)
                    return Task.FromResult(new SignalScoringResult(signal.Confidence, features));
                prediction = _predictionEngine.Predict(
                    SignalScoringFeatureCatalog.ToModelInput(features));
            }
            var mlScore = (decimal)Math.Clamp(prediction.Probability, 0f, 1f);
            var weight = (decimal)_settings.MlScoreBlendWeight;
            var blended = Math.Clamp(
                mlScore * weight + signal.Confidence * (1m - weight), 0m, 1m);
            return Task.FromResult(new SignalScoringResult(blended, features));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "시그널 스코어링 실패 — 원래 Confidence 사용");
            return Task.FromResult(new SignalScoringResult(signal.Confidence, features));
        }
    }

    public async Task<bool> TrainAsync(
        IReadOnlyList<SignalScoringTrainingSample> samples,
        CancellationToken ct = default)
    {
        if (samples.Count < _settings.MinTrainingSamples)
        {
            _logger.LogWarning(
                "시그널 스코러 인과적 학습 데이터 부족: {Count}개 (최소 {Min}개)",
                samples.Count, _settings.MinTrainingSamples);
            return false;
        }
        if (!SignalScoringDatasetPolicy.TrySplit(samples, out var split, out var reason))
        {
            _logger.LogWarning("시그널 스코러 학습 거부: {Reason}", reason);
            return false;
        }

        try
        {
            var fitted = await Task.Run(() => _trainer.Fit(split!), ct);
            var manifest = new SignalScorerModelManifest(
                SignalScoringFeatureSchema.CurrentVersion,
                SignalScoringFeatureSchema.FeatureCount,
                _timeProvider.GetUtcNow().UtcDateTime,
                split!.Training.Count + split.Validation.Count,
                fitted.Accuracy,
                fitted.Auc,
                fitted.FeatureImportances,
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
            _logger.LogError(exception, "시그널 스코러 학습 실패");
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

    public SignalScorerStatus GetStatus()
    {
        lock (_modelLock)
        {
            return new SignalScorerStatus(
                _model is not null,
                _trainedAt,
                _trainingSamples,
                _lastAccuracy,
                _lastAuc,
                _featureImportances.ToArray());
        }
    }

    private void Publish(ITransformer model, SignalScorerModelManifest manifest)
    {
        var engine = _mlContext.Model
            .CreatePredictionEngine<SignalScorerInput, SignalScorerOutput>(model);
        lock (_modelLock)
        {
            _model = model;
            _predictionEngine = engine;
            _trainedAt = manifest.TrainedAtUtc;
            _trainingSamples = manifest.TrainingSamples;
            _lastAccuracy = manifest.ValidationAccuracy;
            _lastAuc = manifest.ValidationAuc;
            _featureImportances = manifest.FeatureImportances ?? [];
        }
    }
}

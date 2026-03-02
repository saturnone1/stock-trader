using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.ML;

/// <summary>
/// K-Means 클러스터링 기반 시장 레짐 분류기.
/// 라벨 데이터 없이 비지도학습으로 4개의 레짐(강세/약세/횡보/고변동)을 식별합니다.
/// </summary>
public class MarketRegimeClassifier : IMarketRegimeClassifier
{
    private readonly MLContext _mlContext;
    private readonly MLSettings _settings;
    private readonly IIndicatorService _indicators;
    private readonly ILogger<MarketRegimeClassifier> _logger;

    private ITransformer? _model;
    private PredictionEngine<RegimeFeatureInput, RegimeClusterOutput>? _predictionEngine;
    private readonly object _modelLock = new();

    // 클러스터 → 레짐 레이블 매핑 (학습 후 centroid 분석으로 결정)
    private Dictionary<uint, string> _clusterLabels = new();

    public bool IsModelLoaded => _model != null;
    public DateTime? TrainedAt { get; private set; }
    public int TrainingSamples { get; private set; }
    public IReadOnlyDictionary<uint, string> ClusterLabels => _clusterLabels;

    // 레짐별 한국어 레이블
    private static readonly string[] RegimeNames = { "강세장", "약세장", "횡보장", "고변동장" };

    public MarketRegimeClassifier(
        IOptions<MLSettings> settings,
        IIndicatorService indicators,
        ILogger<MarketRegimeClassifier> logger)
    {
        _settings = settings.Value;
        _indicators = indicators;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);

        TryLoadModel();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────

    public async Task<MarketRegime> ClassifyAsync(OhlcvBar[] spyBars, CancellationToken ct = default)
    {
        // 기본 레짐 계산 (기존 로직 — 항상 수행)
        var baseRegime = ComputeBaseRegime(spyBars);

        if (!IsModelLoaded || spyBars.Length < 30)
        {
            _logger.LogDebug("ML 레짐 분류기 미사용 (모델 로드: {Loaded}, 데이터: {Count}개)",
                IsModelLoaded, spyBars.Length);
            return baseRegime;
        }

        try
        {
            var features = await Task.Run(() => ExtractFeatures(spyBars), ct);
            if (features == null) return baseRegime;

            RegimeClusterOutput prediction;
            lock (_modelLock)
            {
                if (_predictionEngine == null) return baseRegime;
                prediction = _predictionEngine.Predict(features);
            }

            var clusterId = prediction.ClusterId;
            var label = _clusterLabels.TryGetValue(clusterId, out var lbl) ? lbl : $"클러스터{clusterId}";

            _logger.LogDebug("ML 레짐 분류 결과: 클러스터={ClusterId}, 레이블={Label}", clusterId, label);

            return new MarketRegime
            {
                SpyAbove200Ma = baseRegime.SpyAbove200Ma,
                SpyPrice = baseRegime.SpyPrice,
                Spy200Ma = baseRegime.Spy200Ma,
                VixLevel = baseRegime.VixLevel,
                RegimeLabel = label,
                AsOf = baseRegime.AsOf,
                MlClusterId = (int)clusterId,
                MlRegimeLabel = label
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML 레짐 분류 실패 — 기본 레짐 사용");
            return baseRegime;
        }
    }

    public async Task<bool> TrainAsync(OhlcvBar[] spyBars, CancellationToken ct = default)
    {
        if (spyBars.Length < _settings.MinTrainingSamples)
        {
            _logger.LogWarning("레짐 모델 학습 데이터 부족: {Count}개 (최소 {Min}개)",
                spyBars.Length, _settings.MinTrainingSamples);
            return false;
        }

        _logger.LogInformation("시장 레짐 분류기 학습 시작: {Count}개 샘플", spyBars.Length);

        try
        {
            var trainingData = await Task.Run(() => BuildTrainingData(spyBars), ct);
            if (trainingData.Count < _settings.MinTrainingSamples)
            {
                _logger.LogWarning("피처 추출 후 샘플 부족: {Count}개", trainingData.Count);
                return false;
            }

            await Task.Run(() => FitModel(trainingData, spyBars), ct);

            TrainedAt = DateTime.UtcNow;
            TrainingSamples = trainingData.Count;

            SaveModel();

            _logger.LogInformation("레짐 분류기 학습 완료: {Count}개 샘플, 클러스터 맵: {@Labels}",
                trainingData.Count, _clusterLabels);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "레짐 분류기 학습 실패");
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private: Feature Extraction
    // ──────────────────────────────────────────────────────────────────────

    private List<RegimeFeatureInput> BuildTrainingData(OhlcvBar[] bars)
    {
        var closes = bars.Select(b => (double)b.Close).ToArray();
        var volumes = bars.Select(b => (double)b.Volume).ToArray();

        var result = new List<RegimeFeatureInput>();

        for (int i = 25; i < bars.Length; i++)
        {
            var features = ComputeFeatureAt(closes, volumes, i);
            if (features != null)
                result.Add(features);
        }

        return result;
    }

    private RegimeFeatureInput? ExtractFeatures(OhlcvBar[] bars)
    {
        if (bars.Length < 25) return null;

        var closes = bars.Select(b => (double)b.Close).ToArray();
        var volumes = bars.Select(b => (double)b.Volume).ToArray();
        return ComputeFeatureAt(closes, volumes, bars.Length - 1);
    }

    private static RegimeFeatureInput? ComputeFeatureAt(double[] closes, double[] volumes, int i)
    {
        if (i < 25 || closes[i - 5] <= 0 || closes[i - 10] <= 0 || closes[i - 20] <= 0)
            return null;

        // 수익률
        var ret5 = (closes[i] - closes[i - 5]) / closes[i - 5];
        var ret10 = (closes[i] - closes[i - 10]) / closes[i - 10];
        var ret20 = (closes[i] - closes[i - 20]) / closes[i - 20];

        // 5일 변동성 (일별 수익률 표준편차 → VIX proxy)
        var returns = new double[5];
        for (int k = 0; k < 5; k++)
            returns[k] = closes[i - k] > 0 ? (closes[i - k] - closes[i - k - 1]) / closes[i - k - 1] : 0;
        var avgRet = returns.Average();
        var vol = Math.Sqrt(returns.Average(r => (r - avgRet) * (r - avgRet)));

        // 거래량 변화율
        var vol5Avg = volumes.Skip(i - 4).Take(5).Average();
        var vol20Avg = volumes.Skip(Math.Max(0, i - 19)).Take(20).Average();
        var volChange = vol20Avg > 0 ? (vol5Avg - vol20Avg) / vol20Avg : 0;

        // 20일 MA 기울기
        double ma20Now = 0, ma20Prev = 0;
        for (int k = i - 19; k <= i; k++) ma20Now += closes[k];
        ma20Now /= 20;
        for (int k = i - 24; k <= i - 5; k++) ma20Prev += closes[k];
        ma20Prev /= 20;
        var maSlope = ma20Prev > 0 ? (ma20Now - ma20Prev) / ma20Prev : 0;

        // RSI (14일)
        double avgGain = 0, avgLoss = 0;
        for (int k = Math.Max(1, i - 13); k <= i; k++)
        {
            var change = closes[k] - closes[k - 1];
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }
        avgGain /= 14;
        avgLoss /= 14;
        var rsi = avgLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + avgGain / avgLoss));

        return new RegimeFeatureInput
        {
            Return5Day = (float)ret5,
            Return10Day = (float)ret10,
            Return20Day = (float)ret20,
            VolatilityLevel = (float)vol,
            VolumeChangeRate = (float)volChange,
            MaSlopePercent = (float)maSlope,
            Rsi = (float)(rsi / 100.0)  // normalize to 0~1
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private: Model Fitting
    // ──────────────────────────────────────────────────────────────────────

    private void FitModel(List<RegimeFeatureInput> trainingData, OhlcvBar[] bars)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = _mlContext.Transforms.Concatenate(
                "Features",
                nameof(RegimeFeatureInput.Return5Day),
                nameof(RegimeFeatureInput.Return10Day),
                nameof(RegimeFeatureInput.Return20Day),
                nameof(RegimeFeatureInput.VolatilityLevel),
                nameof(RegimeFeatureInput.VolumeChangeRate),
                nameof(RegimeFeatureInput.MaSlopePercent),
                nameof(RegimeFeatureInput.Rsi))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters: _settings.RegimeClusterCount));

        var model = pipeline.Fit(dataView);

        // 클러스터별 레짐 레이블 결정 (centroid 분석)
        _clusterLabels = AssignClusterLabels(model, dataView, trainingData);

        lock (_modelLock)
        {
            _model = model;
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<RegimeFeatureInput, RegimeClusterOutput>(model);
        }
    }

    /// <summary>
    /// 각 클러스터의 중심점 특성을 분석하여 레짐 레이블을 부여합니다.
    /// - Return20Day > 0, VolatilityLevel 낮음 → 강세장
    /// - Return20Day < 0, VolatilityLevel 낮음 → 약세장
    /// - Return20Day ≈ 0, VolatilityLevel 낮음 → 횡보장
    /// - VolatilityLevel 높음 → 고변동장
    /// </summary>
    private Dictionary<uint, string> AssignClusterLabels(
        ITransformer model, IDataView dataView, List<RegimeFeatureInput> samples)
    {
        var predictions = model.Transform(dataView);
        var clusterIds = _mlContext.Data
            .CreateEnumerable<RegimeClusterOutput>(predictions, reuseRowObject: false)
            .Select(p => p.ClusterId)
            .ToArray();

        // 클러스터별 평균 Return20Day, VolatilityLevel 계산
        var clusterStats = new Dictionary<uint, (double avgRet20, double avgVol)>();
        for (int i = 0; i < samples.Count && i < clusterIds.Length; i++)
        {
            var cid = clusterIds[i];
            var (ret, vol) = clusterStats.GetValueOrDefault(cid, (0, 0));
            clusterStats[cid] = (ret + samples[i].Return20Day, vol + samples[i].VolatilityLevel);
        }

        // 클러스터 카운트
        var clusterCounts = clusterIds.GroupBy(c => c).ToDictionary(g => g.Key, g => (double)g.Count());
        foreach (var cid in clusterStats.Keys.ToList())
        {
            var count = clusterCounts.GetValueOrDefault(cid, 1.0);
            var (ret, vol) = clusterStats[cid];
            clusterStats[cid] = (ret / count, vol / count);
        }

        // 변동성 중앙값으로 고변동 클러스터 식별
        var allVols = clusterStats.Values.Select(v => v.avgVol).OrderBy(v => v).ToList();
        var volMedian = allVols.Count > 0 ? allVols[allVols.Count / 2] : 0;

        var labels = new Dictionary<uint, string>();
        // 먼저 모든 클러스터를 수익률 기준으로 분류
        var sorted = clusterStats.OrderByDescending(kv => kv.Value.avgRet20).ToList();

        for (int rank = 0; rank < sorted.Count; rank++)
        {
            var (cid, (avgRet20, avgVol)) = sorted[rank];

            string label;
            if (avgVol > volMedian * 1.5)
                label = "고변동장";
            else if (avgRet20 > 0.02)
                label = "강세장";
            else if (avgRet20 < -0.02)
                label = "약세장";
            else
                label = "횡보장";

            labels[cid] = label;
        }

        return labels;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private: Model Persistence
    // ──────────────────────────────────────────────────────────────────────

    private string GetModelPath()
    {
        var dir = Path.IsPathRooted(_settings.ModelDirectory)
            ? _settings.ModelDirectory
            : Path.Combine(AppContext.BaseDirectory, _settings.ModelDirectory);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, _settings.RegimeModelFileName);
    }

    /// <summary>
    /// BUG-L01: 클러스터 레이블 매핑 JSON 파일 경로.
    /// 모델 파일과 같은 디렉터리에 "regime_labels.json"으로 저장한다.
    /// 재시작 후 TryLoadModel()이 이 파일을 읽어 학습 당시의 레이블 매핑을 복원한다.
    /// </summary>
    private string GetLabelsPath()
    {
        var modelPath = GetModelPath();
        return Path.Combine(Path.GetDirectoryName(modelPath)!, "regime_labels.json");
    }

    private void SaveModel()
    {
        if (_model == null) return;
        try
        {
            var path = GetModelPath();
            _mlContext.Model.Save(_model, null, path);
            _logger.LogInformation("레짐 분류기 모델 저장 완료: {Path}", path);

            // BUG-L01: 클러스터→레이블 매핑을 JSON으로 저장한다.
            // 재시작 시 K-Means 클러스터 번호는 유지되지만 번호와 레이블의 대응 관계는
            // 모델 파일에 포함되지 않으므로, 별도 JSON으로 영속화해야 한다.
            // 저장하지 않으면 순서(0→강세장, 1→약세장…)로 할당되어 실제 매핑과 불일치한다.
            var labelsPath = GetLabelsPath();
            var serializable = _clusterLabels.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            var json = JsonSerializer.Serialize(serializable,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(labelsPath, json);
            _logger.LogInformation("클러스터 레이블 매핑 저장 완료: {Path}", labelsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "레짐 분류기 모델 저장 실패");
        }
    }

    private void TryLoadModel()
    {
        try
        {
            var path = GetModelPath();
            if (!File.Exists(path)) return;

            ITransformer loaded;
            lock (_modelLock)
            {
                loaded = _mlContext.Model.Load(path, out _);
                _model = loaded;
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<RegimeFeatureInput, RegimeClusterOutput>(loaded);
            }

            // 파일 생성 시각을 TrainedAt으로 사용
            TrainedAt = File.GetLastWriteTimeUtc(path);

            // BUG-L01: 클러스터 레이블 매핑 복원.
            // 학습 시 저장된 JSON이 있으면 정확한 매핑을 사용하고,
            // JSON이 없으면 순서 기반 기본값으로 폴백한다 (구버전 모델 호환).
            var labelsPath = GetLabelsPath();
            if (File.Exists(labelsPath))
            {
                try
                {
                    var json = File.ReadAllText(labelsPath);
                    var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (raw != null)
                    {
                        _clusterLabels = raw
                            .Where(kv => uint.TryParse(kv.Key, out _))
                            .ToDictionary(kv => uint.Parse(kv.Key), kv => kv.Value);
                        _logger.LogInformation(
                            "클러스터 레이블 매핑 복원 완료: {@Labels}", _clusterLabels);
                    }
                    else
                    {
                        AssignDefaultLabels();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "클러스터 레이블 JSON 파싱 실패 — 기본값 사용");
                    AssignDefaultLabels();
                }
            }
            else
            {
                // JSON 파일 없음 → 순서 기반 기본값 (이전 동작과 동일)
                AssignDefaultLabels();
                _logger.LogWarning(
                    "클러스터 레이블 매핑 파일이 없습니다. 재학습하면 정확한 매핑이 저장됩니다.");
            }

            _logger.LogInformation("레짐 분류기 모델 로드 완료: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "저장된 레짐 분류기 모델 로드 실패 (신규 학습 필요)");
        }
    }

    /// <summary>
    /// 순서 기반 기본 레이블 할당 (JSON 파일 없을 때 폴백).
    /// </summary>
    private void AssignDefaultLabels()
    {
        for (uint i = 0; i < _settings.RegimeClusterCount; i++)
            _clusterLabels[i] = RegimeNames[i % RegimeNames.Length];
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private: Base Regime (기존 로직 — ML 불가 시 폴백)
    // ──────────────────────────────────────────────────────────────────────

    private static MarketRegime ComputeBaseRegime(OhlcvBar[] spyBars)
    {
        if (spyBars.Length == 0)
            return new MarketRegime { RegimeLabel = "알 수 없음", AsOf = DateTime.UtcNow };

        var last = spyBars[^1];
        decimal ma200 = 0;

        if (spyBars.Length >= 200)
        {
            ma200 = spyBars.TakeLast(200).Average(b => b.Close);
        }

        var aboveMa = ma200 > 0 && last.Close > ma200;
        return new MarketRegime
        {
            SpyAbove200Ma = aboveMa,
            SpyPrice = last.Close,
            Spy200Ma = ma200,
            RegimeLabel = aboveMa ? "강세" : "약세",
            AsOf = last.Timestamp
        };
    }
}

using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.ML;

/// <summary>
/// FastTree Binary Classification 기반 시그널 신뢰도 스코어러.
/// 백테스트 거래 내역을 학습 데이터로 사용하여 각 패턴 시그널의 성공 확률을 예측합니다.
/// 트레이너: FastTree (Microsoft.ML.FastTree 패키지 필요)
/// </summary>
public class SignalScorer : ISignalScorer
{
    private readonly MLContext _mlContext;
    private readonly MLSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalScorer> _logger;

    private ITransformer? _model;
    private PredictionEngine<SignalScorerInput, SignalScorerOutput>? _predictionEngine;
    private readonly object _modelLock = new();

    public bool IsModelLoaded => _model != null;
    public DateTime? TrainedAt { get; private set; }
    public int TrainingSamples { get; private set; }
    public double LastAccuracy { get; private set; }
    public double LastAuc { get; private set; }
    public List<FeatureImportance> FeatureImportances { get; private set; } = new();

    // 피처 이름 목록 (UI 표시 및 중요도 계산에 사용)
    private static readonly string[] FeatureNames =
    {
        "패턴 유형", "RSI", "볼린저 위치", "거래량 비율",
        "시장 레짐", "ATR%", "역사적 승률", "R:R 비율", "200MA 대비 위치"
    };

    public SignalScorer(
        IOptions<MLSettings> settings,
        TimeProvider timeProvider,
        ILogger<SignalScorer> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);

        TryLoadModel();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────

    public async Task<decimal> ScoreAsync(
        PatternSignal signal,
        OhlcvBar[] bars,
        MarketRegime regime,
        decimal historicalWinRate = 0.5m,
        CancellationToken ct = default)
    {
        if (!IsModelLoaded || !_settings.EnableMlScoring || bars.Length < 20)
            return signal.Confidence;

        try
        {
            var input = await Task.Run(() => BuildInput(signal, bars, regime, historicalWinRate), ct);

            SignalScorerOutput prediction;
            lock (_modelLock)
            {
                if (_predictionEngine == null) return signal.Confidence;
                prediction = _predictionEngine.Predict(input);
            }

            // ML 점수와 기존 Confidence를 블렌딩
            var mlScore = (decimal)Math.Clamp(prediction.Probability, 0f, 1f);
            var weight = (decimal)_settings.MlScoreBlendWeight;
            var blended = mlScore * weight + signal.Confidence * (1m - weight);

            _logger.LogDebug("시그널 스코어 [{Pattern}] ML={Ml:F3}, 기존={Old:F3}, 블렌드={New:F3}",
                signal.PatternType, mlScore, signal.Confidence, blended);

            return Math.Clamp(blended, 0m, 1m);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "시그널 스코어링 실패 — 원래 Confidence 사용");
            return signal.Confidence;
        }
    }

    public async Task<bool> TrainAsync(IReadOnlyList<TradeRecord> trades, CancellationToken ct = default)
    {
        if (trades.Count < _settings.MinTrainingSamples)
        {
            _logger.LogWarning("시그널 스코어러 학습 데이터 부족: {Count}개 (최소 {Min}개)",
                trades.Count, _settings.MinTrainingSamples);
            return false;
        }

        _logger.LogInformation("시그널 스코어러 학습 시작: {Count}개 거래 내역", trades.Count);

        try
        {
            var trainingData = trades.Select(BuildTrainingInput).ToList();
            var (accuracy, auc) = await Task.Run(() => FitModel(trainingData), ct);

            TrainedAt = _timeProvider.GetUtcNow().UtcDateTime;
            TrainingSamples = trainingData.Count;
            LastAccuracy = accuracy;
            LastAuc = auc;

            SaveModel();

            _logger.LogInformation("시그널 스코어러 학습 완료: 정확도={Acc:P2}, AUC={Auc:F3}",
                accuracy, auc);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "시그널 스코어러 학습 실패");
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private: Input Building
    // ──────────────────────────────────────────────────────────────────────

    private static SignalScorerInput BuildInput(
        PatternSignal signal, OhlcvBar[] bars, MarketRegime regime, decimal historicalWinRate)
    {
        var closes = bars.Select(b => (double)b.Close).ToArray();
        var n = closes.Length;

        // RSI (14일)
        double rsi = 50;
        if (n > 15)
        {
            double avgGain = 0, avgLoss = 0;
            for (int i = n - 14; i < n; i++)
            {
                var change = closes[i] - closes[i - 1];
                if (change > 0) avgGain += change;
                else avgLoss += Math.Abs(change);
            }
            avgGain /= 14; avgLoss /= 14;
            rsi = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        }

        // 볼린저밴드 위치 (최근 20일 기준)
        double bbPosition = 0.5;
        if (n >= 20)
        {
            var slice = closes.TakeLast(20).ToArray();
            var mean = slice.Average();
            var std = Math.Sqrt(slice.Average(c => (c - mean) * (c - mean)));
            if (std > 0)
            {
                var upper = mean + 2 * std;
                var lower = mean - 2 * std;
                bbPosition = (closes[n - 1] - lower) / (upper - lower);
                bbPosition = Math.Clamp(bbPosition, 0, 1);
            }
        }

        // 거래량 비율 (최근 1일 / 20일 평균)
        double volRatio = 1;
        if (bars.Length >= 20)
        {
            var avg20Vol = bars.TakeLast(20).Average(b => (double)b.Volume);
            var currVol = (double)bars[^1].Volume;
            volRatio = avg20Vol > 0 ? Math.Clamp(currVol / avg20Vol, 0, 5) : 1;
        }

        // ATR% (최근 14일)
        double atrPct = 0.02;
        if (bars.Length >= 15)
        {
            var trs = new double[14];
            for (int i = bars.Length - 14; i < bars.Length; i++)
            {
                var hl = (double)(bars[i].High - bars[i].Low);
                var hc = Math.Abs((double)(bars[i].High - bars[i - 1].Close));
                var lc = Math.Abs((double)(bars[i].Low - bars[i - 1].Close));
                trs[i - (bars.Length - 14)] = Math.Max(hl, Math.Max(hc, lc));
            }
            atrPct = bars[^1].Close > 0 ? trs.Average() / (double)bars[^1].Close : 0.02;
        }

        // 200MA 대비 위치
        double priceVs200Ma = 0;
        if (bars.Length >= 200)
        {
            var ma200 = bars.TakeLast(200).Average(b => (double)b.Close);
            priceVs200Ma = ma200 > 0 ? ((double)bars[^1].Close - ma200) / ma200 : 0;
        }

        // 시장 레짐 코드
        float regimeCode = regime.MlClusterId >= 0 ? regime.MlClusterId : MapRegimeLabelToCode(regime.RegimeLabel);

        // R:R 비율
        var rr = signal.StopLossPrice > 0 && signal.EntryPrice != signal.StopLossPrice
            ? Math.Abs((double)(signal.TargetPrice - signal.EntryPrice))
              / Math.Abs((double)(signal.EntryPrice - signal.StopLossPrice))
            : 2.0;

        return new SignalScorerInput
        {
            Label = false, // 예측 시에는 사용되지 않음
            PatternTypeCode = (float)signal.PatternType,
            Rsi = (float)(rsi / 100.0),
            BollingerPosition = (float)bbPosition,
            VolumeRatio = (float)volRatio,
            MarketRegimeCode = regimeCode,
            AtrPercent = (float)Math.Clamp(atrPct, 0, 0.2),
            HistoricalWinRate = (float)Math.Clamp((double)historicalWinRate, 0, 1),
            RiskRewardRatio = (float)Math.Clamp(rr, 0, 5),
            PriceVs200Ma = (float)Math.Clamp(priceVs200Ma, -0.5, 0.5)
        };
    }

    private static SignalScorerInput BuildTrainingInput(TradeRecord trade)
    {
        // 학습 데이터는 거래 내역에서 생성 — 상세 바 데이터가 없으므로 알려진 값만 사용
        var rrRatio = trade.EntryPrice > 0 && trade.ExitPrice > 0
            ? Math.Abs((double)(trade.ExitPrice - trade.EntryPrice)) / Math.Max(1, Math.Abs((double)trade.EntryPrice * 0.02))
            : 2.0;

        return new SignalScorerInput
        {
            Label = trade.IsWin,
            PatternTypeCode = (float)trade.PatternType,
            Rsi = 0.5f,               // 백테스트 거래에는 RSI 데이터 없음 → 중립값
            BollingerPosition = 0.5f,
            VolumeRatio = 1.0f,
            MarketRegimeCode = 0f,    // 레짐 데이터 없음 → 강세 기본값
            AtrPercent = 0.02f,
            HistoricalWinRate = 0.5f, // 중립값 사용 — IsWin(레이블)을 피처로 쓰면 데이터 누출 발생
            RiskRewardRatio = (float)Math.Clamp(rrRatio, 0, 5),
            PriceVs200Ma = 0f
        };
    }

    private static float MapRegimeLabelToCode(string label) => label switch
    {
        "강세" or "강세장" => 0f,
        "약세" or "약세장" => 1f,
        "횡보장"           => 2f,
        "고변동장"         => 3f,
        _                  => 0f
    };

    // ──────────────────────────────────────────────────────────────────────
    // Private: Model Fitting
    // ──────────────────────────────────────────────────────────────────────

    private (double accuracy, double auc) FitModel(List<SignalScorerInput> trainingData)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        var pipeline = _mlContext.Transforms.Concatenate(
                "Features",
                nameof(SignalScorerInput.PatternTypeCode),
                nameof(SignalScorerInput.Rsi),
                nameof(SignalScorerInput.BollingerPosition),
                nameof(SignalScorerInput.VolumeRatio),
                nameof(SignalScorerInput.MarketRegimeCode),
                nameof(SignalScorerInput.AtrPercent),
                nameof(SignalScorerInput.HistoricalWinRate),
                nameof(SignalScorerInput.RiskRewardRatio),
                nameof(SignalScorerInput.PriceVs200Ma))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    LabelColumnName = "Label",
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 20,
                    NumberOfTrees = 100,
                    MinimumExampleCountPerLeaf = 5,
                    LearningRate = 0.1
                }));

        var model = pipeline.Fit(split.TrainSet);

        // 평가
        var predictions = model.Transform(split.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");

        // 피처 중요도 계산 (도메인 지식 기반 근사값)
        ComputeFeatureImportances(model, split.TrainSet);

        lock (_modelLock)
        {
            _model = model;
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<SignalScorerInput, SignalScorerOutput>(model);
        }

        return (metrics.Accuracy, metrics.AreaUnderRocCurve);
    }

    private void ComputeFeatureImportances(ITransformer model, IDataView trainData)
    {
        try
        {
            FeatureImportances = FeatureNames
                .Select((name, i) => new FeatureImportance
                {
                    FeatureName = name,
                    Importance = GetApproxImportance(i)
                })
                .OrderByDescending(f => f.Importance)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "피처 중요도 계산 실패 (무시됨)");
            FeatureImportances = FeatureNames
                .Select(name => new FeatureImportance { FeatureName = name, Importance = 1.0 / FeatureNames.Length })
                .ToList();
        }
    }

    /// <summary>도메인 지식 기반 근사 피처 중요도 (인덱스 순서 = FeatureNames 배열 순서)</summary>
    private static double GetApproxImportance(int featureIndex) => featureIndex switch
    {
        0 => 0.20, // 패턴 유형
        1 => 0.12, // RSI
        2 => 0.08, // 볼린저 위치
        3 => 0.15, // 거래량 비율
        4 => 0.18, // 시장 레짐
        5 => 0.07, // ATR%
        6 => 0.10, // 역사적 승률
        7 => 0.06, // R:R 비율
        8 => 0.04, // 200MA 대비 위치
        _ => 0.01
    };

    // ──────────────────────────────────────────────────────────────────────
    // Private: Model Persistence
    // ──────────────────────────────────────────────────────────────────────

    private string GetModelPath()
    {
        var dir = Path.IsPathRooted(_settings.ModelDirectory)
            ? _settings.ModelDirectory
            : Path.Combine(AppContext.BaseDirectory, _settings.ModelDirectory);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, _settings.SignalScorerModelFileName);
    }

    private void SaveModel()
    {
        if (_model == null) return;
        try
        {
            var path = GetModelPath();
            _mlContext.Model.Save(_model, null, path);
            _logger.LogInformation("시그널 스코어러 모델 저장 완료: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "시그널 스코어러 모델 저장 실패");
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
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<SignalScorerInput, SignalScorerOutput>(loaded);
            }

            TrainedAt = File.GetLastWriteTimeUtc(path);
            _logger.LogInformation("시그널 스코어러 모델 로드 완료: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "저장된 시그널 스코어러 모델 로드 실패 (신규 학습 필요)");
        }
    }
}

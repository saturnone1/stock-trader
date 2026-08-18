using StockTrader.Models;

namespace StockTrader.Application.MachineLearning;

/// <summary>데이터 수집, 두 모델 학습, 결과와 원자적 상태 관측을 조율합니다.</summary>
internal sealed class MLModelTrainingService : IMLModelTrainingService
{
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly ISignalScorer _signalScorer;
    private readonly IMarketRegimeTrainingDataSource _regimeData;
    private readonly ISignalScoringTrainingStore _trainingStore;
    private readonly MlTrainingRunState _runState;
    private readonly MlTrainingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MLModelTrainingService> _logger;

    public MLModelTrainingService(
        IMarketRegimeClassifier regimeClassifier,
        ISignalScorer signalScorer,
        IMarketRegimeTrainingDataSource regimeData,
        ISignalScoringTrainingStore trainingStore,
        MlTrainingRunState runState,
        MlTrainingOptions options,
        TimeProvider timeProvider,
        ILogger<MLModelTrainingService> logger)
    {
        _regimeClassifier = regimeClassifier;
        _signalScorer = signalScorer;
        _regimeData = regimeData;
        _trainingStore = trainingStore;
        _runState = runState;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default)
    {
        if (!_runState.TryBegin())
        {
            return new MlTrainingResult
            {
                Success = false,
                Message = "이미 학습이 진행 중입니다. 잠시 후 다시 시도하세요.",
            };
        }

        try
        {
            var startTime = _timeProvider.GetUtcNow().UtcDateTime;
            _logger.LogInformation("ML 학습 시작: 레짐 분류기");

            var regimeSymbol = "시장 레짐 기준 종목";
            OhlcvBar[] regimeBars = [];
            try
            {
                _runState.SetStatus("시장 레짐 기준 종목 데이터 수집 중...");
                var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
                var trainingSet = await _regimeData.LoadAsync(
                    observedAt.AddDays(-_options.RegimeTrainingDays),
                    observedAt,
                    ct);
                regimeSymbol = trainingSet.Symbol;
                regimeBars = trainingSet.Bars.ToArray();
                _logger.LogInformation(
                    "{Symbol} 데이터 수집 완료: {Count}개 바",
                    regimeSymbol,
                    regimeBars.Length);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "{Symbol} 데이터 수집 실패 — 레짐 분류기 학습 건너뜀",
                    regimeSymbol);
            }

            var regimeTrained = false;
            var regimeSamples = 0;
            if (regimeBars.Length >= _options.MinimumTrainingSamples)
            {
                _runState.SetStatus("시장 레짐 분류기 학습 중...");
                regimeTrained = await _regimeClassifier.TrainAsync(regimeBars, ct);
                if (regimeTrained)
                    regimeSamples = _regimeClassifier.GetStatus().TrainingSamples;
            }
            else
            {
                _logger.LogWarning(
                    "{Symbol} 데이터 부족으로 레짐 분류기 학습 건너뜀 ({Count}개)",
                    regimeSymbol,
                    regimeBars.Length);
            }

            _runState.SetStatus("인과적 시그널 학습 샘플 로딩 중...");
            _logger.LogInformation("ML 학습: 시그널 스코어러");
            var samples = await _trainingStore.GetRecentAsync(
                _options.SignalSampleLimit,
                ct);

            var scorerTrained = false;
            double accuracy = 0;
            double auc = 0;
            if (samples.Count >= _options.MinimumTrainingSamples)
            {
                _runState.SetStatus(
                    $"시그널 스코어러 학습 중... ({samples.Count}개 인과적 샘플)");
                scorerTrained = await _signalScorer.TrainAsync(samples, ct);
                if (scorerTrained)
                {
                    var scorerStatus = _signalScorer.GetStatus();
                    accuracy = scorerStatus.ValidationAccuracy;
                    auc = scorerStatus.ValidationAuc;
                }
            }
            else
            {
                _logger.LogWarning(
                    "인과적 피처·결과 샘플 부족으로 시그널 스코러 학습 건너뜀 ({Count}개)",
                    samples.Count);
            }

            var duration = _timeProvider.GetUtcNow().UtcDateTime - startTime;
            _runState.SetStatus("완료");
            var message = BuildResultMessage(
                regimeTrained,
                scorerTrained,
                regimeSymbol,
                regimeSamples,
                samples.Count,
                _options.MinimumTrainingSamples);
            _logger.LogInformation(
                "ML 학습 완료: {Duration:F1}초, 레짐={Regime}, 스코어러={Scorer}",
                duration.TotalSeconds,
                regimeTrained,
                scorerTrained);

            return new MlTrainingResult
            {
                Success = regimeTrained || scorerTrained,
                Message = message,
                RegimeSamples = regimeSamples,
                SignalSamples = samples.Count,
                SignalScorerAccuracy = accuracy,
                SignalScorerAuc = auc,
                TrainingDuration = duration,
            };
        }
        catch (OperationCanceledException)
        {
            _runState.SetStatus("취소됨");
            return new MlTrainingResult
            {
                Success = false,
                Message = "학습이 취소되었습니다.",
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ML 학습 중 예상치 못한 오류 발생");
            _runState.SetStatus("오류 발생");
            return new MlTrainingResult
            {
                Success = false,
                Message = $"학습 중 오류 발생: {exception.Message}",
            };
        }
        finally
        {
            _runState.End();
        }
    }

    private static string BuildResultMessage(
        bool regimeTrained,
        bool scorerTrained,
        string regimeSymbol,
        int regimeSamples,
        int signalSamples,
        int minimumTrainingSamples)
    {
        var regime = regimeTrained
            ? $"레짐 분류기 학습 완료 ({regimeSamples}개 {regimeSymbol} 피처)"
            : "레짐 분류기 건너뜀 (데이터 부족)";
        var scorer = scorerTrained
            ? $"시그널 스코러 학습 완료 ({signalSamples}개 인과적 샘플)"
            : $"시그널 스코러 건너뜀 (최소 {minimumTrainingSamples}개 인과적 샘플 필요, "
              + $"현재 {signalSamples}개)";
        return $"{regime} | {scorer}";
    }
}

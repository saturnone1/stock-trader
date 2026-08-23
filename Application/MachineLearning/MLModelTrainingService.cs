using StockTrader.ServiceContracts.MachineLearning;

namespace StockTrader.Application.MachineLearning;

/// <summary>인과적 입력 준비와 검증된 학습 아티팩트 승격만 조율합니다.</summary>
internal sealed class MLModelTrainingService : IMLModelTrainingService
{
    private readonly IMarketRegimeClassifier _regimeClassifier;
    private readonly ISignalScorer _signalScorer;
    private readonly IMarketRegimeTrainingDataSource _regimeData;
    private readonly ISignalScoringTrainingStore _trainingStore;
    private readonly IMlTrainingTransport _transport;
    private readonly MlTrainingRunState _runState;
    private readonly MlTrainingOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MLModelTrainingService> _logger;

    public MLModelTrainingService(
        IMarketRegimeClassifier regimeClassifier,
        ISignalScorer signalScorer,
        IMarketRegimeTrainingDataSource regimeData,
        ISignalScoringTrainingStore trainingStore,
        IMlTrainingTransport transport,
        MlTrainingRunState runState,
        MlTrainingOptions options,
        TimeProvider clock,
        ILogger<MLModelTrainingService> logger)
    {
        _regimeClassifier = regimeClassifier;
        _signalScorer = signalScorer;
        _regimeData = regimeData;
        _trainingStore = trainingStore;
        _transport = transport;
        _runState = runState;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<MlTrainingResult> TrainAllAsync(CancellationToken ct = default)
    {
        if (!_runState.TryBegin())
            return Failure("이미 학습이 진행 중입니다. 잠시 후 다시 시도하세요.");

        var started = _clock.GetUtcNow().UtcDateTime;
        try
        {
            _runState.SetStatus("학습 근거 데이터 준비 중...");
            var regime = await _regimeData.LoadAsync(
                started.AddDays(-_options.RegimeTrainingDays), started, ct);
            var signals = await _trainingStore.GetRecentAsync(
                _options.SignalSampleLimit, ct);
            _runState.SetStatus("ML Training 서비스에서 모델 학습 중...");
            var result = await _transport.TrainAsync(
                regime, signals, _options, started, ct);

            if (result.Status is MlTrainingJobStatuses.Failed
                or MlTrainingJobStatuses.Cancelled)
                return Failure(result.Message);

            var regimeImported = result.RegimeArtifact is not null
                && _regimeClassifier.ImportArtifact(result.RegimeArtifact);
            var signalImported = result.SignalArtifact is not null
                && _signalScorer.ImportArtifact(result.SignalArtifact);
            if (result.RegimeArtifact is not null && !regimeImported)
                throw new InvalidOperationException("검증된 레짐 아티팩트를 추론 캐시에 승격하지 못했습니다.");
            if (result.SignalArtifact is not null && !signalImported)
                throw new InvalidOperationException("검증된 시그널 아티팩트를 추론 캐시에 승격하지 못했습니다.");

            var duration = _clock.GetUtcNow().UtcDateTime - started;
            _runState.SetStatus("완료");
            _logger.LogInformation(
                "ML Training job {JobId} completed: {Status}, revision={Revision}",
                result.JobId, result.Status, result.PublicationRevision);
            return new MlTrainingResult
            {
                Success = regimeImported || signalImported,
                Message = BuildMessage(result, signals.Count, _options.MinimumTrainingSamples),
                RegimeSamples = result.RegimeArtifact?.TrainingSamples ?? 0,
                SignalSamples = signals.Count,
                SignalScorerAccuracy = result.SignalArtifact?.ValidationAccuracy ?? 0,
                SignalScorerAuc = result.SignalArtifact?.ValidationAuc ?? 0,
                TrainingDuration = duration,
            };
        }
        catch (OperationCanceledException)
        {
            _runState.SetStatus("취소됨");
            return Failure("학습이 취소되었습니다.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "ML 학습·승격 실패");
            _runState.SetStatus("오류 발생");
            return Failure($"학습 중 오류 발생: {exception.Message}");
        }
        finally
        {
            _runState.End();
        }
    }

    private static MlTrainingResult Failure(string message) => new()
    {
        Success = false,
        Message = message,
    };

    private static string BuildMessage(
        MlTrainingJobResult result, int signalSamples, int minimumTrainingSamples)
    {
        var regime = result.RegimeArtifact is null
            ? "레짐 분류기 건너뜀 (데이터 부족)"
            : $"레짐 분류기 학습 완료 ({result.RegimeArtifact.TrainingSamples}개 피처)";
        var signal = result.SignalArtifact is null
            ? $"시그널 스코러 건너뜀 (최소 {minimumTrainingSamples}개 필요, 현재 인과적 샘플 {signalSamples}개)"
            : $"시그널 스코러 학습 완료 ({result.SignalArtifact.TrainingSamples}개 인과적 샘플)";
        return $"{regime} | {signal}";
    }
}

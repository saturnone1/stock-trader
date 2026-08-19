using Microsoft.Extensions.Options;
using StockTrader.Application.Analysis;
using StockTrader.Application.MachineLearning;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Notification;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 설정된 주기로 ML 모델을 자동 재학습하는 백그라운드 서비스.
/// 설정된 ET 허용 시각 이후에만 실행하여 거래 시간 중 리소스 경합을 방지합니다.
/// </summary>
public sealed class MLRetrainingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationDispatcher _dispatcher;
    private readonly MLSettings _mlSettings;
    private readonly IMarketCalendar _marketCalendar;
    private readonly TimeProvider _timeProvider;
    private readonly TimeOnly _retrainAfterEt;
    private readonly ILogger<MLRetrainingService> _logger;

    private int _consecutiveFailures;

    public MLRetrainingService(
        IServiceScopeFactory scopeFactory,
        INotificationDispatcher dispatcher,
        IOptions<MLSettings> mlSettings,
        IMarketCalendar marketCalendar,
        TimeProvider timeProvider,
        ILogger<MLRetrainingService> logger)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _mlSettings = mlSettings.Value;
        _marketCalendar = marketCalendar;
        _timeProvider = timeProvider;
        _retrainAfterEt = TimeOnly.ParseExact(_mlSettings.AutoRetrainAfterEt, "HH:mm");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MLRetrainingService started. Retrain interval: {Hours}h, runs after {Time} ET",
            _mlSettings.AutoRetrainIntervalHours, _retrainAfterEt);

        // 초기 지연: 다음 설정된 ET 허용 시각까지 대기
        var initialDelay = CalculateDelayToNextWindow();
        _logger.LogInformation(
            "MLRetrainingService: first retrain in {Hours:F1} hours", initialDelay.TotalHours);
        await Task.Delay(initialDelay, _timeProvider, stoppingToken);

        // 첫 실행
        if (!stoppingToken.IsCancellationRequested)
            await RunRetrainCycleAsync(stoppingToken);

        // 이후 주기적 실행. 매 주기 뒤 ET 창을 다시 계산해 DST 전환 후에도
        // 16시로 밀린 타이머가 영구적으로 재학습을 건너뛰지 않도록 한다.
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(
                MlRetrainingSchedulePolicy.CalculateRecurringDelay(
                    _timeProvider.GetUtcNow(),
                    TimeSpan.FromHours(_mlSettings.AutoRetrainIntervalHours),
                    EasternTime,
                    _retrainAfterEt,
                    TradingDayPredicate),
                _timeProvider,
                stoppingToken);
            await RunRetrainCycleAsync(stoppingToken);
        }
    }

    private async Task RunRetrainCycleAsync(CancellationToken stoppingToken)
    {
        var observedAt = _timeProvider.GetUtcNow();
        var nowEt = TimeZoneInfo.ConvertTime(observedAt, EasternTime);
        var window = MlRetrainingSchedulePolicy.Evaluate(
            observedAt,
            EasternTime,
            _retrainAfterEt,
            TradingDayPredicate);

        // 휴장일 스킵: 새로 완성된 거래 결과가 없어 재학습할 근거가 없다.
        if (window == MlRetrainingWindowStatus.NonTradingDay)
        {
            _logger.LogDebug("MLRetrainingService: skipping — non-trading day");
            return;
        }

        // 설정된 ET 허용 시각 전이면 스킵
        if (window == MlRetrainingWindowStatus.BeforeDailyWindow)
        {
            _logger.LogDebug(
                "MLRetrainingService: skipping — before market close ({Time} ET)", nowEt.TimeOfDay);
            return;
        }

        // Circuit breaker
        if (_consecutiveFailures >= _mlSettings.AutoRetrainMaxConsecutiveFailures)
        {
            _logger.LogWarning(
                "MLRetrainingService entering cooldown after {Failures} consecutive failures",
                _consecutiveFailures);
            await Task.Delay(
                TimeSpan.FromMinutes(_mlSettings.AutoRetrainCooldownMinutes),
                _timeProvider,
                stoppingToken);
            _consecutiveFailures = 0;
        }

        try
        {
            await RetryHelper.ExecuteWithRetryAsync(
                () => ExecuteRetrainAsync(stoppingToken),
                _logger,
                "MLRetrain",
                maxRetries: _mlSettings.AutoRetrainMaxRetries,
                ct: stoppingToken);

            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 정상 종료
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogError(ex,
                "ML retraining failed (consecutive failures: {Failures})", _consecutiveFailures);
        }
    }

    private async Task ExecuteRetrainAsync(CancellationToken ct)
    {
        _logger.LogInformation("ML auto-retrain cycle starting");

        using var scope = _scopeFactory.CreateScope();
        var trainingService = scope.ServiceProvider
            .GetRequiredService<IMLModelTrainingService>();

        var result = await trainingService.TrainAllAsync(ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "ML auto-retrain completed in {Duration:F1}s — " +
                "Regime samples={RegimeSamples}, Signal samples={SignalSamples}, " +
                "Accuracy={Accuracy:P1}",
                result.TrainingDuration.TotalSeconds,
                result.RegimeSamples,
                result.SignalSamples,
                result.SignalScorerAccuracy);

            await _dispatcher.DispatchAlertAsync(
                $"ML 모델 자동 재학습 완료 ({result.TrainingDuration.TotalSeconds:F0}초)\n" +
                $"레짐 샘플: {result.RegimeSamples}, 시그널 샘플: {result.SignalSamples}\n" +
                $"스코어러 정확도: {result.SignalScorerAccuracy:P1}",
                ct);
        }
        else
        {
            _logger.LogWarning("ML auto-retrain completed with issues: {Message}", result.Message);

            await _dispatcher.DispatchAlertAsync(
                $"ML 모델 자동 재학습 실패: {result.Message}", ct);
        }
    }

    private TimeSpan CalculateDelayToNextWindow()
        => MlRetrainingSchedulePolicy.CalculateInitialDelay(
            _timeProvider.GetUtcNow(),
            EasternTime,
            _retrainAfterEt,
            TradingDayPredicate);


    private Func<DateOnly, bool> TradingDayPredicate =>
        _marketCalendar.TradingDayPredicate(MarketRegion.UnitedStates);

    private TimeZoneInfo EasternTime =>
        _marketCalendar.GetTimeZone(MarketRegion.UnitedStates);

}

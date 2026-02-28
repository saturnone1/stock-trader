using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.ML;
using StockTrader.Services.Notification;
using TimeZoneConverter;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 설정된 주기(기본 24시간)로 ML 모델을 자동 재학습하는 백그라운드 서비스.
/// 시장 종료 후(17:00 ET 이후)에만 실행하여 거래 시간 중 리소스 경합을 방지합니다.
/// </summary>
public sealed class MLRetrainingService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetrainAfterET = TimeSpan.FromHours(17);

    private static readonly TimeZoneInfo EasternTime =
        TZConvert.GetTimeZoneInfo("America/New_York");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationDispatcher _dispatcher;
    private readonly MLSettings _mlSettings;
    private readonly ILogger<MLRetrainingService> _logger;

    private int _consecutiveFailures;

    public MLRetrainingService(
        IServiceScopeFactory scopeFactory,
        INotificationDispatcher dispatcher,
        IOptions<MLSettings> mlSettings,
        ILogger<MLRetrainingService> logger)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _mlSettings = mlSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MLRetrainingService started. Retrain interval: {Hours}h, runs after {Time} ET",
            _mlSettings.AutoRetrainIntervalHours, RetrainAfterET);

        // 초기 지연: 다음 적절한 시점(17:00 ET 이후)까지 대기
        var initialDelay = CalculateDelayToNextWindow();
        _logger.LogInformation(
            "MLRetrainingService: first retrain in {Hours:F1} hours", initialDelay.TotalHours);
        await Task.Delay(initialDelay, stoppingToken);

        // 첫 실행
        if (!stoppingToken.IsCancellationRequested)
            await RunRetrainCycleAsync(stoppingToken);

        // 이후 주기적 실행
        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(_mlSettings.AutoRetrainIntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunRetrainCycleAsync(stoppingToken);
        }
    }

    private async Task RunRetrainCycleAsync(CancellationToken stoppingToken)
    {
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTime);

        // 주말 스킵
        if (nowEt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            _logger.LogDebug("MLRetrainingService: skipping — weekend");
            return;
        }

        // 시장 시간 중이면 스킵 (17:00 ET 이전)
        if (nowEt.TimeOfDay < RetrainAfterET)
        {
            _logger.LogDebug(
                "MLRetrainingService: skipping — before market close ({Time} ET)", nowEt.TimeOfDay);
            return;
        }

        // Circuit breaker
        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _logger.LogWarning(
                "MLRetrainingService entering cooldown after {Failures} consecutive failures",
                _consecutiveFailures);
            await Task.Delay(CooldownPeriod, stoppingToken);
            _consecutiveFailures = 0;
        }

        try
        {
            await RetryHelper.ExecuteWithRetryAsync(
                () => ExecuteRetrainAsync(stoppingToken),
                _logger,
                "MLRetrain",
                maxRetries: 3,
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
    {
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTime);

        // 평일 17:00 ET 이후이면 거의 즉시 시작
        if (nowEt.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            && nowEt.TimeOfDay >= RetrainAfterET)
        {
            return TimeSpan.FromMinutes(1);
        }

        // 오늘 또는 다음 영업일의 17:00 ET까지 대기
        var nextWindow = nowEt.Date + RetrainAfterET;
        if (nowEt.TimeOfDay >= RetrainAfterET)
            nextWindow = nextWindow.AddDays(1);

        // 주말 스킵
        while (nextWindow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            nextWindow = nextWindow.AddDays(1);

        var nextWindowUtc = TimeZoneInfo.ConvertTimeToUtc(nextWindow, EasternTime);
        var delay = nextWindowUtc - DateTime.UtcNow;

        return delay < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay;
    }
}

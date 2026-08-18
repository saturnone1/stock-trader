using Microsoft.Extensions.Options;
using StockTrader.Application.Risk;
using StockTrader.Configuration;
using StockTrader.Services.Notification;
using StockTrader.Services.Risk;

namespace StockTrader.BackgroundServices;

public class RiskMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly TradingSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RiskMonitorService> _logger;

    private int _consecutiveFailures = 0;
    private DateTime _lastHaltAlertUtc = DateTime.MinValue;

    public RiskMonitorService(
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        IOptions<TradingSettings> settings,
        TimeProvider timeProvider,
        ILogger<RiskMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _notificationService = notificationService;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RiskMonitorService started");

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_settings.RiskCheckIntervalSeconds),
            _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Circuit breaker: cooldown when too many consecutive failures.
            // Risk monitoring is critical — log at Warning so it is visible immediately.
            if (_consecutiveFailures >= _settings.RiskMonitorMaxConsecutiveFailures)
            {
                var cooldown = TimeSpan.FromSeconds(_settings.RiskMonitorCooldownSeconds);
                _logger.LogWarning(
                    "{Service} entering cooldown after {Failures} consecutive failures. " +
                    "RISK MONITORING IS SUSPENDED for {Cooldown}",
                    nameof(RiskMonitorService), _consecutiveFailures, cooldown);

                await Task.Delay(cooldown, _timeProvider, stoppingToken);
                _consecutiveFailures = 0;
            }

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => RunRiskCheckAsync(stoppingToken),
                    _logger,
                    "RiskMonitor",
                    maxRetries: 3,
                    ct: stoppingToken);

                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;

                // Risk monitoring failures are Critical: a silent failure could allow
                // unchecked losses to accumulate without halting trading.
                _logger.LogCritical(ex,
                    "CRITICAL: Risk monitoring failed (consecutive failures: {Failures}). " +
                    "Portfolio may be unprotected",
                    _consecutiveFailures);
            }
        }
    }

    private async Task RunRiskCheckAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var riskService = scope.ServiceProvider
            .GetRequiredService<IRiskManagementService>();

        await riskService.UpdateDailyPnLAsync(ct);
        var state = await riskService.GetCurrentRiskStateAsync(ct);

        _logger.LogDebug(
            "Risk check: PnL={PnL:C2} ({PnLPct:P2}), Positions={Positions}, Halted={Halted}",
            state.DailyPnL, state.DailyPnLPercent, state.OpenPositionCount, state.IsTradingHalted);

        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        if (RiskAlertPolicy.IsDue(
                state.IsTradingHalted,
                _lastHaltAlertUtc,
                observedAt,
                TimeSpan.FromMinutes(_settings.RiskHaltAlertIntervalMinutes)))
        {
            _notificationService.Alert(
                $"거래 중단: 일일 손실 {state.DailyPnLPercent:P2}이(가) 한도를 초과했습니다");
            _lastHaltAlertUtc = observedAt;
        }
    }
}

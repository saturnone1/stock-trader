using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>일봉 동기화 유스케이스의 시작 복구, 주기 실행, 복원 정책만 담당합니다.</summary>
public sealed class DailyDataSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<DailyDataSyncService> logger) : BackgroundService
{
    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DailyDataSyncService started");
        await RunInitialSyncAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(settings.Value.DailyDataSyncIntervalMinutes);
        using var timer = new PeriodicTimer(interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (Volatile.Read(ref _consecutiveFailures)
                >= settings.Value.DailyDataSyncMaxConsecutiveFailures)
            {
                var cooldown = TimeSpan.FromSeconds(
                    settings.Value.DailyDataSyncCooldownSeconds);
                logger.LogWarning(
                    "{Service} entering cooldown after {Failures} consecutive failures. "
                    + "Waiting {Cooldown} before resuming",
                    nameof(DailyDataSyncService),
                    _consecutiveFailures,
                    cooldown);
                await Task.Delay(cooldown, timeProvider, stoppingToken);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => RunScheduledCycleAsync(stoppingToken),
                    logger,
                    "DailyDataSync",
                    maxRetries: settings.Value.DailyDataSyncMaxRetries,
                    ct: stoppingToken,
                    timeProvider: timeProvider);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _consecutiveFailures);
                logger.LogError(
                    exception,
                    "Error during daily data sync (consecutive failures: {Failures})",
                    Volatile.Read(ref _consecutiveFailures));
            }
        }
    }

    private async Task RunInitialSyncAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cycle = scope.ServiceProvider.GetRequiredService<IDailyMarketDataSyncCycle>();
        await cycle.RunInitialSyncIfNeededAsync(ct);
    }

    private async Task RunScheduledCycleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cycle = scope.ServiceProvider.GetRequiredService<IDailyMarketDataSyncCycle>();
        await cycle.RunScheduledAsync(ct);
    }
}

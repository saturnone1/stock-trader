using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>REST 분봉 수집 유스케이스의 주기 실행, 스트리밍 전환, 복원 정책만 담당합니다.</summary>
public sealed class MarketDataIngestionService(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<MarketDataIngestionService> logger) : BackgroundService
{
    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MarketDataIngestionService started");
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.Value.DataFetchIntervalSeconds),
            timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (Volatile.Read(ref _consecutiveFailures)
                >= settings.Value.IntradayDataMaxConsecutiveFailures)
            {
                var cooldown = TimeSpan.FromSeconds(
                    settings.Value.IntradayDataCooldownSeconds);
                logger.LogWarning(
                    "{Service} entering cooldown after {Failures} consecutive failures. "
                    + "Waiting {Cooldown} before resuming",
                    nameof(MarketDataIngestionService),
                    _consecutiveFailures,
                    cooldown);
                await Task.Delay(cooldown, timeProvider, stoppingToken);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => RunCycleAsync(stoppingToken),
                    logger,
                    "MarketDataIngestion",
                    maxRetries: settings.Value.IntradayDataMaxRetries,
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
                    "Error during market data ingestion (consecutive failures: {Failures})",
                    Volatile.Read(ref _consecutiveFailures));
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cycle = scope.ServiceProvider
            .GetRequiredService<IIntradayMarketDataIngestionCycle>();
        await cycle.RunAsync(ct);
    }
}

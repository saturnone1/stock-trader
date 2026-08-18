using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockTrader.Application.Trading;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>수신된 종목을 재시도·회로 차단 정책과 함께 일봉 스캔 유스케이스로 전달합니다.</summary>
public sealed class PatternScannerService(
    IServiceScopeFactory scopeFactory,
    Channel<string> symbolChannel,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<PatternScannerService> logger) : BackgroundService
{
    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PatternScannerService started");

        try
        {
            await foreach (var symbol in symbolChannel.Reader.ReadAllAsync(stoppingToken))
            {
                if (Volatile.Read(ref _consecutiveFailures)
                    >= settings.Value.PatternScanMaxConsecutiveFailures)
                {
                    var cooldown = TimeSpan.FromSeconds(
                        settings.Value.PatternScanCooldownSeconds);
                    logger.LogWarning(
                        "{Service} entering cooldown after {Failures} consecutive failures. "
                        + "Waiting {Cooldown} before resuming",
                        nameof(PatternScannerService),
                        _consecutiveFailures,
                        cooldown);
                    await Task.Delay(cooldown, timeProvider, stoppingToken);
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                }

                try
                {
                    await RetryHelper.ExecuteWithRetryAsync(
                        () => RunCycleAsync(symbol, stoppingToken),
                        logger,
                        $"PatternScan({symbol})",
                        maxRetries: settings.Value.PatternScanMaxRetries,
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
                        "Error scanning patterns for {Symbol} (consecutive failures: {Failures})",
                        symbol,
                        Volatile.Read(ref _consecutiveFailures));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("PatternScannerService stopping due to cancellation");
        }
        catch (ChannelClosedException exception)
        {
            logger.LogError(
                exception,
                "Symbol channel was closed unexpectedly; PatternScannerService is stopping");
        }
    }

    private async Task RunCycleAsync(string symbol, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cycle = scope.ServiceProvider.GetRequiredService<ILivePatternScanCycle>();
        await cycle.RunAsync(symbol, ct);
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>재시작 뒤에도 미확정 신규 진입을 브로커 주문 내역과 재조정한다.</summary>
public sealed class EntryExecutionReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<EntryExecutionReconciliationService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("EntryExecutionReconciliationService started");
        var interval = TimeSpan.FromSeconds(
            settings.Value.EntryReconciliationIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var cycle = scope.ServiceProvider
                    .GetRequiredService<ILiveEntryReconciliationCycle>();
                await cycle.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pending entry reconciliation cycle failed");
            }

            await Task.Delay(interval, timeProvider, stoppingToken);
        }
        logger.LogInformation("EntryExecutionReconciliationService stopped");
    }
}

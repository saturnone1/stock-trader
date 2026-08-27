using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.TradingCore;

namespace StockTrader.BackgroundServices;

public sealed class TradingCorePositionShadowService(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingCoreTransportOptions> options,
    TimeProvider clock,
    ILogger<TradingCorePositionShadowService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode != "Shadow") return;
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.ShadowComparisonIntervalSeconds), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TradingCorePositionShadowCycle>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error,
                    "Trading Core position Shadow cycle failed; Local financial authority unchanged");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

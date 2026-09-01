using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

public sealed class TradingCoreAccountConfigurationPublisherService(
    IServiceScopeFactory scopeFactory,
    ITradingCoreControlPlane controlPlane,
    IOptions<TradingCoreTransportOptions> options,
    TimeProvider clock,
    ILogger<TradingCoreAccountConfigurationPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.ProjectionIntervalSeconds), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var source = scope.ServiceProvider
                    .GetRequiredService<ITradingCoreAccountConfigurationSource>();
                var configuration = await source.CaptureAsync(stoppingToken);
                await controlPlane.PublishAccountConfigurationAsync(configuration, stoppingToken);
                logger.LogDebug(
                    "Trading Core account configuration generation {Generation} published",
                    configuration.Generation);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Trading Core account configuration publication failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

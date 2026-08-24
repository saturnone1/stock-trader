using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

public sealed class TradingCoreProjectionService(
    IServiceScopeFactory scopeFactory,
    ITradingCoreControlPlane controlPlane,
    IOptions<TradingCoreTransportOptions> options,
    TimeProvider clock,
    ILogger<TradingCoreProjectionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Mode is "Local" or "Remote") return;
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.ProjectionIntervalSeconds), clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var accountSource = scope.ServiceProvider
                    .GetRequiredService<ITradingCoreAccountConfigurationSource>();
                var source = scope.ServiceProvider.GetRequiredService<ITradingCoreProjectionSource>();
                var accountConfiguration = await accountSource.CaptureAsync(stoppingToken);
                await controlPlane.PublishAccountConfigurationAsync(
                    accountConfiguration, stoppingToken);
                var snapshot = await source.CaptureAsync(stoppingToken);
                var duplicate = await controlPlane.PublishProjectionAsync(snapshot, stoppingToken);
                logger.LogInformation(
                    "Trading Core projection {SnapshotId} published (duplicate={Duplicate})",
                    snapshot.SnapshotId, duplicate);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Trading Core projection failed; Local financial authority unchanged");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

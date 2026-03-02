using System.Threading.Channels;
using StockTrader.BackgroundServices;

namespace StockTrader.Extensions;

public static class BackgroundServiceExtensions
{
    public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
    {
        // Inter-service communication channel
        services.AddSingleton(Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

        // Hosted services
        services.AddHostedService<AlpacaStreamingService>();
        services.AddHostedService<MarketDataIngestionService>();
        services.AddHostedService<PatternScannerService>();
        services.AddHostedService<DailyDataSyncService>();
        services.AddHostedService<RiskMonitorService>();
        services.AddHostedService<PositionExitManagerService>();
        services.AddHostedService<DailyReportService>();
        services.AddHostedService<MLRetrainingService>();

        return services;
    }
}

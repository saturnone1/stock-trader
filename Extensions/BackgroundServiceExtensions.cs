using System.Threading.Channels;
using StockTrader.BackgroundServices;

namespace StockTrader.Extensions;

public static class BackgroundServiceExtensions
{
    public static IServiceCollection AddBackgroundServices(
        this IServiceCollection services,
        bool includeHostedServices = true)
    {
        // Inter-service communication channel
        services.AddSingleton(Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

        // Components called by endpoints or other use cases are registered independently from
        // hosted execution so build-time API contract generation cannot start external loops.
        services.AddSingleton<OptimizationJobExecutor>();
        services.AddSingleton<FinancialSnapshotIngestionService>();

        if (!includeHostedServices)
            return services;

        services.AddHostedService<AlpacaStreamingService>();
        services.AddHostedService<MarketDataIngestionService>();
        services.AddHostedService<PatternScannerService>();
        services.AddHostedService<DailyDataSyncService>();
        services.AddHostedService<RiskMonitorService>();
        services.AddHostedService<PositionExecutionManagerService>();
        services.AddHostedService<DailyReportService>();
        services.AddHostedService<MLRetrainingService>();
        services.AddHostedService<ContinuousOptimizationService>();
        services.AddHostedService(sp => sp.GetRequiredService<FinancialSnapshotIngestionService>());

        return services;
    }
}

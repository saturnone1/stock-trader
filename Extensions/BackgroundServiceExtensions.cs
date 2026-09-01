using System.Threading.Channels;
using StockTrader.Application.Optimization;
using StockTrader.BackgroundServices;
using StockTrader.Configuration;
using Microsoft.Extensions.Options;

namespace StockTrader.Extensions;

public static class BackgroundServiceExtensions
{
    public static IServiceCollection AddBackgroundServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool includeHostedServices = true)
    {
        // Inter-service communication channel
        services.AddSingleton(Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

        // Components called by endpoints or other use cases are registered independently from
        // hosted execution so build-time API contract generation cannot start external loops.
        services.AddSingleton<OptimizationJobExecutor>();
        services.AddSingleton<RemoteOptimizationJobExecutor>();
        services.AddSingleton<IOptimizationWorkExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<OptimizationWorkerTransportOptions>>()
                .Value.Mode == OptimizationWorkerTransportMode.Remote
                ? serviceProvider.GetRequiredService<RemoteOptimizationJobExecutor>()
                : serviceProvider.GetRequiredService<OptimizationJobExecutor>());
        services.AddSingleton<FinancialSnapshotIngestionService>();

        if (!includeHostedServices)
            return services;

        var tradingCoreMode = configuration[$"{TradingCoreTransportOptions.SectionName}:Mode"]
            ?? "Local";
        var isRemote = string.Equals(tradingCoreMode, "Remote", StringComparison.Ordinal);
        var publishesToTradingCore = !string.Equals(
            tradingCoreMode, "Local", StringComparison.Ordinal);

        if (!isRemote)
        {
            services.AddHostedService<AlpacaStreamingService>();
            services.AddHostedService<MarketDataSubscriptionSyncService>();
            services.AddHostedService<MarketDataShadowBackfillService>();
            services.AddHostedService<MarketDataIngestionService>();
            services.AddHostedService<DailyDataSyncService>();
        }
        services.AddHostedService<PatternScannerService>();
        if (!isRemote)
        {
            services.AddHostedService<RiskMonitorService>();
            services.AddHostedService<EntryExecutionReconciliationService>();
            services.AddHostedService<PositionExecutionManagerService>();
        }
        services.AddHostedService<DailyReportService>();
        services.AddHostedService<MLRetrainingService>();
        services.AddHostedService<MlTrainingPublicationReconciliationService>();
        if (publishesToTradingCore)
            services.AddHostedService<TradingCoreAccountConfigurationPublisherService>();
        if (publishesToTradingCore && !isRemote)
            services.AddHostedService<TradingCoreProjectionService>();
        if (string.Equals(tradingCoreMode, "Shadow", StringComparison.Ordinal))
            services.AddHostedService<TradingCorePositionShadowService>();
        services.AddHostedService<ContinuousOptimizationService>();
        services.AddHostedService(sp => sp.GetRequiredService<FinancialSnapshotIngestionService>());

        return services;
    }
}

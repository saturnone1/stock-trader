using StockTrader.Configuration;
using StockTrader.Application.Strategies;
using StockTrader.Application.Optimization;
using StockTrader.Application.Settings;
using StockTrader.Application.Signals;
using StockTrader.Application.Research;
using StockTrader.Application.Accounts;
using StockTrader.Application.Execution;
using StockTrader.Application.Reporting;
using StockTrader.Application.Dashboard;
using StockTrader.Application.Trading;
using StockTrader.Application.MarketData;
using StockTrader.Application.TradingCore;
using StockTrader.Data.Repositories;
using StockTrader.Data.Migrations;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Financial;
using StockTrader.Services.LsSecurities;
using StockTrader.Services.Streaming;

namespace StockTrader.Extensions;

public static class DataServiceExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Repositories
        services.AddScoped<OhlcvRepository>();
        services.AddScoped<RemoteOhlcvRepository>();
        services.AddScoped<LocalMarketDataBarWriter>();
        services.AddScoped<RemoteMarketDataBarWriter>();
        services.AddScoped<IMarketDataBarWriter, MarketDataBarWriterRouter>();
        services.AddScoped<IOhlcvRepository, MarketDataRepositoryRouter>();
        services.AddScoped<MarketDataRollbackProjector>();
        services.AddScoped<IPatternStatsRepository, PatternStatsRepository>();
        services.AddSingleton<ITradeHistoryStore, TradeHistoryStore>();
        services.AddSingleton<IOpenPositionStore, OpenPositionStore>();
        services.AddSingleton<ITradeRecommendationStore, TradeRecommendationStore>();
        services.AddSingleton<ITradeActivityStore, TradeActivityStore>();
        services.AddSingleton<IPatternSignalStore, PatternSignalStore>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISettingsManagementStore, SettingsManagementStore>();
        services.AddScoped<SettingsManagementService>();
        services.AddScoped<IOptimizationJobExecutionStore, OptimizationJobExecutionStore>();
        services.AddScoped<IOptimizationJobLifecycle, OptimizationJobLifecycle>();
        services.AddScoped<OptimizationWorkerLeaseCoordinator>();
        services.AddScoped<IOptimizationWorkerLeaseCoordinator>(services =>
            services.GetRequiredService<OptimizationWorkerLeaseCoordinator>());
        services.AddScoped<IOptimizationShadowResultCoordinator>(services =>
            services.GetRequiredService<OptimizationWorkerLeaseCoordinator>());
        services.AddScoped<IOptimizationWorkerLeaseMonitor>(services =>
            services.GetRequiredService<OptimizationWorkerLeaseCoordinator>());
        services.AddScoped<IOptimizationRemoteResultCommitter>(services =>
            services.GetRequiredService<OptimizationWorkerLeaseCoordinator>());
        services.AddScoped<OptimizationShadowResultRecorder>();
        services.AddScoped<OptimizationShadowLeasePublisher>();
        services.AddScoped<IOptimizationJobControlStore, OptimizationJobControlStore>();
        services.AddScoped<OptimizationJobControlService>();
        services.AddScoped<IOptimizationJobManagementStore, OptimizationJobManagementStore>();
        services.AddScoped<OptimizationJobManagementService>();
        services.AddScoped<IOptimizationAutoTuneStore, OptimizationAutoTuneStore>();
        services.AddScoped<OptimizationAutoTuneService>();
        services.AddScoped<ICompiledStrategyRepository, CompiledStrategyRepository>();
        services.AddScoped<ICustomPatternStore, CustomPatternStore>();
        services.AddScoped<ILiveSignalEvaluationStore, LiveSignalEvaluationStore>();
        services.AddSingleton<IResearchUniverseStore, ResearchUniverseStore>();
        services.AddSingleton<IFinancialCollectionStore, FinancialCollectionStore>();
        services.AddSingleton<ITradingAccountStore, TradingAccountStore>();
        services.AddSingleton<IManualOrderSignalStore, ManualOrderSignalStore>();
        services.AddSingleton<ILiveEntryExecutionStore, LiveEntryExecutionStore>();
        services.AddSingleton<ILivePositionExecutionStore, LivePositionExecutionStore>();
        services.AddSingleton<IDailyReportActivityStore, DailyReportActivityStore>();
        services.AddSingleton<IDashboardActivityStore, DashboardActivityStore>();
        services.AddScoped<ITradingCoreProjectionSource, TradingCoreProjectionSource>();
        services.AddScoped<ITradingCoreAccountConfigurationSource, TradingCoreAccountConfigurationSource>();
        services.AddScoped<ITradingAccountIdentitySource, TradingAccountIdentitySource>();
        services.AddScoped<DatabaseSchemaMigrator>();
        services.AddScoped<DatabaseMigrationStatusProvider>();

        // Data Feed - Keyed services for multiple providers
        services.AddScoped<AlpacaDataFeedService>();
        services.AddHttpClient<MarketDataServiceClient>((serviceProvider, client) =>
        {
            var transport = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataTransportOptions>>()
                .Value;
            client.BaseAddress = transport.Endpoint;
            client.Timeout = TimeSpan.FromSeconds(transport.TimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            MarketDataServiceClient.CreateHandler(serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataTransportOptions>>()
                .Value));

        // HttpClient for Yahoo Finance
        services.AddHttpClient<YahooFinanceDataFeedService>(client =>
        {
            var yahooConfig = configuration.GetSection("YahooFinance").Get<YahooFinanceSettings>()
                              ?? new YahooFinanceSettings();
            client.BaseAddress = new Uri(yahooConfig.BaseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", yahooConfig.UserAgent);
        });

        services.AddHttpClient<SecFinancialSnapshotSyncService>(client =>
        {
            var pipelineConfig = configuration.GetSection("FinancialDataPipeline").Get<FinancialDataPipelineSettings>()
                                 ?? new FinancialDataPipelineSettings();
            client.BaseAddress = new Uri("https://www.sec.gov");
            client.DefaultRequestHeaders.Add("User-Agent", pipelineConfig.VendorUserAgent);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // LS Securities 공통 인증 (Singleton: 토큰을 앱 전체에서 공유)
        services.AddSingleton<LsAuthService>();

        // HttpClient + Keyed DI for LS Securities
        services.AddHttpClient<LsSecuritiesDataFeedService>();
        services.AddKeyedScoped<IDataFeedService>(DataSource.Alpaca, (sp, _) =>
            new MarketDataFeedRouter(DataSource.Alpaca,
                sp.GetRequiredService<AlpacaDataFeedService>(),
                sp.GetRequiredService<MarketDataServiceClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataTransportOptions>>()));
        services.AddKeyedScoped<IDataFeedService>(DataSource.Yahoo, (sp, _) =>
            new MarketDataFeedRouter(DataSource.Yahoo,
                sp.GetRequiredService<YahooFinanceDataFeedService>(),
                sp.GetRequiredService<MarketDataServiceClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataTransportOptions>>()));
        services.AddKeyedScoped<IDataFeedService>(DataSource.LsSecurities, (sp, _) =>
            new MarketDataFeedRouter(DataSource.LsSecurities,
                sp.GetRequiredService<LsSecuritiesDataFeedService>(),
                sp.GetRequiredService<MarketDataServiceClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataTransportOptions>>()));

        // Data Feed Factory for runtime provider switching
        services.AddScoped<IDataFeedServiceFactory, DataFeedServiceFactory>();
        services.AddScoped<ILiveDailyScanData, LiveDailyScanData>();
        services.AddScoped<IDailyMarketDataSyncData, DailyMarketDataSyncData>();
        services.AddScoped<IIntradayMarketDataIngestionData, IntradayMarketDataIngestionData>();
        services.AddScoped<IIntradayMarketDataIngestionCycle, IntradayMarketDataIngestionCycle>();
        services.AddScoped<IRealtimeMarketDataSelectionReader, RealtimeMarketDataSelectionReader>();
        services.AddSingleton<IRealtimeBarBatchSink, RealtimeBarBatchSink>();
        services.AddSingleton<IRealtimeBarIngestionBuffer, RealtimeBarIngestionBuffer>();

        return services;
    }
}

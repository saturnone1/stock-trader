using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using StockTrader.BackgroundServices;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.Services.Risk;
using StockTrader.Services.Signal;
using StockTrader.Services.Streaming;
using StockTrader.Services.Analysis;
using StockTrader.Services.Statistics;

namespace StockTrader.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockTraderServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<AlpacaSettings>(configuration.GetSection("Alpaca"));
        services.Configure<PolygonSettings>(configuration.GetSection("Polygon"));
        services.Configure<YahooFinanceSettings>(configuration.GetSection("YahooFinance"));
        services.Configure<TradingSettings>(configuration.GetSection("Trading"));
        services.Configure<PatternSettings>(configuration.GetSection("Patterns"));

        // Database (AddDbContextFactory also registers AppDbContext as scoped)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")),
            ServiceLifetime.Scoped);

        // Repositories
        services.AddScoped<IOhlcvRepository, OhlcvRepository>();
        services.AddScoped<IPatternStatsRepository, PatternStatsRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        // Data Feed - Keyed services for multiple providers
        services.AddKeyedScoped<IDataFeedService, AlpacaDataFeedService>(DataSource.Alpaca);
        services.AddKeyedScoped<IDataFeedService, YahooFinanceDataFeedService>(DataSource.Yahoo);

        // HttpClient for Yahoo Finance
        services.AddHttpClient<YahooFinanceDataFeedService>(client =>
        {
            var yahooConfig = configuration.GetSection("YahooFinance").Get<YahooFinanceSettings>()
                              ?? new YahooFinanceSettings();
            client.BaseAddress = new Uri(yahooConfig.BaseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", yahooConfig.UserAgent);
        });

        // Data Feed Factory for runtime provider switching
        services.AddScoped<IDataFeedServiceFactory, DataFeedServiceFactory>();

        // Default IDataFeedService via factory (backward compatibility)
        services.AddScoped<IDataFeedService>(sp =>
        {
            var settingsRepo = sp.GetRequiredService<ISettingsRepository>();
            var settings = settingsRepo.GetAsync().GetAwaiter().GetResult();
            var factory = sp.GetRequiredService<IDataFeedServiceFactory>();
            return factory.GetService(settings.PreferredDataSource);
        });

        // Indicators (stateless - singleton)
        services.AddSingleton<IIndicatorService, IndicatorService>();

        // Pattern Detectors
        services.AddScoped<IPatternDetector, GapUpPullbackDetector>();
        services.AddScoped<IPatternDetector, BreakoutDetector>();
        services.AddScoped<IPatternDetector, VwapReversionDetector>();
        services.AddScoped<IPatternDetector, RsiMeanReversionDetector>();
        services.AddScoped<IPatternDetector, TrendPullbackDetector>();
        services.AddScoped<IPatternDetector, OrbDetector>();
        services.AddScoped<IPatternDetector, VolumeSpikeContinuationDetector>();
        services.AddScoped<IPatternDetector, EarningsDriftDetector>();
        services.AddScoped<IPatternDetector, IndexRegimeFilterDetector>();
        services.AddScoped<IPatternDetector, VolatilityExpansionDetector>();
        services.AddScoped<IPatternDetector, MomentumReversalDetector>();
        services.AddScoped<IPatternDetector, MultiTimeframeTrendDetector>();
        services.AddScoped<IPatternDetector, MeanReversionChannelDetector>();
        services.AddScoped<PatternDetectionService>();

        // Business Services
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<ISignalService, SignalService>();
        services.AddScoped<IRiskManagementService, RiskManagementService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();

        // Notification (singleton for cross-component events)
        services.AddSingleton<INotificationService, InAppNotificationService>();

        // Streaming status (singleton for cross-service coordination)
        services.AddSingleton<IStreamingStatusService, StreamingStatusService>();

        // Inter-service communication channel
        services.AddSingleton(Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));

        // Background Services
        services.AddHostedService<AlpacaStreamingService>();
        services.AddHostedService<MarketDataIngestionService>();
        services.AddHostedService<PatternScannerService>();
        services.AddHostedService<DailyDataSyncService>();
        services.AddHostedService<RiskMonitorService>();

        return services;
    }
}

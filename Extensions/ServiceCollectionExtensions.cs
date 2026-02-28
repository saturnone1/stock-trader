using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.BackgroundServices;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Analysis;
using StockTrader.Services.Backtest;
using StockTrader.Services.Broker;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.ML;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.Services.Risk;
using StockTrader.Services.Signal;
using StockTrader.Services.Statistics;
using StockTrader.Services.Streaming;

namespace StockTrader.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockTraderServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration
        services.Configure<AlpacaSettings>(configuration.GetSection("Alpaca"));
        services.Configure<BrokerSettings>(configuration.GetSection("Broker"));
        services.Configure<PolygonSettings>(configuration.GetSection("Polygon"));
        services.Configure<YahooFinanceSettings>(configuration.GetSection("YahooFinance"));
        services.Configure<TradingSettings>(configuration.GetSection("Trading"));
        services.Configure<PatternSettings>(configuration.GetSection("Patterns"));
        services.Configure<NotificationSettings>(configuration.GetSection("Notification"));
        services.Configure<MLSettings>(configuration.GetSection("ML"));

        // Database (Factory=Singleton for use in singleton services; AppDbContext itself is still scoped)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

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
        services.AddScoped<IPatternDetector, Rsi2BollingerDetector>();
        services.AddScoped<IPatternDetector, VolatilityBreakoutDetector>();
        services.AddScoped<PatternDetectionService>();

        // Broker Services - Keyed DI (DataFeedServiceFactory 패턴과 동일)
        // appsettings 기반 기본 브로커는 하위 호환성을 위해 유지
        services.AddScoped<IBrokerService, AlpacaBrokerService>();
        services.AddKeyedScoped<IBrokerService, AlpacaBrokerService>(BrokerType.Alpaca);
        services.AddKeyedScoped<IBrokerService, KoreaInvestmentBrokerService>(BrokerType.KoreaInvestment);
        services.AddKeyedScoped<IBrokerService, KiwoomBrokerService>(BrokerType.Kiwoom);

        // BrokerServiceFactory — appsettings의 DefaultBrokerType으로 기본 브로커 결정
        services.AddScoped<IBrokerServiceFactory>(sp =>
        {
            var brokerSettings = sp.GetRequiredService<IOptions<BrokerSettings>>().Value;
            return new BrokerServiceFactory(sp, brokerSettings.DefaultBrokerType);
        });

        // AccountManager — 멀티 계좌 관리 (singleton: 계좌 상태는 앱 전체 공유)
        services.AddSingleton<IAccountManager, AccountManager>();

        // ML Services (singleton: 모델을 메모리에 상주 + 재사용)
        services.AddSingleton<IMarketRegimeClassifier, MarketRegimeClassifier>();
        services.AddSingleton<ISignalScorer, SignalScorer>();
        // MLModelTrainingService는 Scoped (DB 레포지터리 의존)
        services.AddScoped<IMLModelTrainingService, MLModelTrainingService>();

        // Business Services
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<ISignalService, SignalService>();
        services.AddScoped<IRiskManagementService, MultiAccountRiskService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();

        // HttpClient factory used by notification channels
        services.AddHttpClient("Telegram");
        services.AddHttpClient("Discord");

        // Notification Channels (singleton: 상태 없음, HttpClient factory로 인스턴스 생성)
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("Telegram");
            var settings = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger = sp.GetRequiredService<ILogger<TelegramNotificationChannel>>();
            return new TelegramNotificationChannel(http, settings, logger);
        });
        services.AddSingleton<INotificationChannel, DiscordNotificationChannel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("Discord");
            var settings = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger = sp.GetRequiredService<ILogger<DiscordNotificationChannel>>();
            return new DiscordNotificationChannel(http, settings, logger);
        });
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>();

        // NotificationDispatcher: 모든 채널에 병렬 발송 (singleton)
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

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
        services.AddHostedService<DailyReportService>();
        services.AddHostedService<MLRetrainingService>();

        return services;
    }
}

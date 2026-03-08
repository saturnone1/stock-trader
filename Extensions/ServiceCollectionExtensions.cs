using Microsoft.EntityFrameworkCore;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Services.Analysis;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Market;
using StockTrader.Services.ML;
using StockTrader.Services.Order;
using StockTrader.Services.Risk;
using StockTrader.Services.LiveParameter;
using StockTrader.Services.Signal;
using StockTrader.Services.Statistics;

namespace StockTrader.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStockTraderServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration binding
        services.Configure<AlpacaSettings>(configuration.GetSection("Alpaca"));
        services.Configure<BrokerSettings>(configuration.GetSection("Broker"));
        services.Configure<YahooFinanceSettings>(configuration.GetSection("YahooFinance"));
        services.Configure<TradingSettings>(configuration.GetSection("Trading"));
        services.Configure<PatternSettings>(configuration.GetSection("Patterns"));
        services.Configure<NotificationSettings>(configuration.GetSection("Notification"));
        services.Configure<MLSettings>(configuration.GetSection("ML"));
        services.Configure<LsSecuritiesSettings>(configuration.GetSection("LsSecurities"));

        // Database
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Domain services
        services.AddDataServices(configuration);
        services.AddBrokerServices();
        services.AddPatternServices();
        services.AddNotificationServices();
        services.AddBackgroundServices();

        // Market Calendar (stateless - singleton)
        services.AddSingleton<IMarketCalendar, MarketCalendar>();

        // Indicators (stateless - singleton)
        services.AddSingleton<IIndicatorService, IndicatorService>();

        // ML Services
        services.AddSingleton<IMarketRegimeClassifier, MarketRegimeClassifier>();
        services.AddSingleton<ISignalScorer, SignalScorer>();
        services.AddSingleton<IMLModelTrainingService, MLModelTrainingService>();

        // Business Services
        services.AddScoped<IStatisticsService, StatisticsService>();
        services.AddScoped<ISignalService, SignalService>();
        services.AddSingleton<IRiskManagementService, MultiAccountRiskService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IBacktestService, BacktestService>();
        services.AddScoped<TqqqWeightBacktester>();
        services.AddScoped<ILiveParameterService, LiveParameterService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();

        return services;
    }
}

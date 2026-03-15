using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Extensions;

public static class DataServiceExtensions
{
    public static IServiceCollection AddDataServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Repositories
        services.AddScoped<IOhlcvRepository, OhlcvRepository>();
        services.AddScoped<IPatternStatsRepository, PatternStatsRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IOptimizationRepository, OptimizationRepository>();

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

        // LS Securities 공통 인증 (Singleton: 토큰을 앱 전체에서 공유)
        services.AddSingleton<LsAuthService>();

        // HttpClient + Keyed DI for LS Securities
        services.AddHttpClient<LsSecuritiesDataFeedService>();
        services.AddKeyedScoped<IDataFeedService>(DataSource.LsSecurities,
            (sp, _) => sp.GetRequiredService<LsSecuritiesDataFeedService>());

        // Data Feed Factory for runtime provider switching
        services.AddScoped<IDataFeedServiceFactory, DataFeedServiceFactory>();

        return services;
    }
}

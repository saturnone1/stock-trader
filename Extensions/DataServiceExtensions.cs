using StockTrader.Configuration;
using StockTrader.Application.Strategies;
using StockTrader.Data.Repositories;
using StockTrader.Data.Migrations;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Financial;
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
        services.AddScoped<ICompiledStrategyRepository, CompiledStrategyRepository>();
        services.AddScoped<DatabaseSchemaMigrator>();
        services.AddScoped<DatabaseMigrationStatusProvider>();

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
        services.AddKeyedScoped<IDataFeedService>(DataSource.LsSecurities,
            (sp, _) => sp.GetRequiredService<LsSecuritiesDataFeedService>());

        // Data Feed Factory for runtime provider switching
        services.AddScoped<IDataFeedServiceFactory, DataFeedServiceFactory>();

        return services;
    }
}

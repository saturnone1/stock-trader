using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Extensions;

public static class BrokerServiceExtensions
{
    public static IServiceCollection AddBrokerServices(this IServiceCollection services)
    {
        // HttpClient for LS Securities Broker
        services.AddHttpClient<LsSecuritiesBrokerService>();

        // Broker Services - Keyed DI (non-keyed 제거: AccountManager가 직접 생성)
        services.AddKeyedScoped<IBrokerService, AlpacaBrokerService>(BrokerType.Alpaca);
        services.AddKeyedScoped<IBrokerService, KoreaInvestmentBrokerService>(BrokerType.KoreaInvestment);
        services.AddKeyedScoped<IBrokerService, KiwoomBrokerService>(BrokerType.Kiwoom);
        services.AddKeyedScoped<IBrokerService>(BrokerType.LsSecurities,
            (sp, _) => sp.GetRequiredService<LsSecuritiesBrokerService>());

        // BrokerServiceFactory
        services.AddScoped<IBrokerServiceFactory>(sp =>
        {
            var brokerSettings = sp.GetRequiredService<IOptions<BrokerSettings>>().Value;
            return new BrokerServiceFactory(sp, brokerSettings.DefaultBrokerType);
        });

        services.AddSingleton<IAccountBrokerServiceFactory, AccountBrokerServiceFactory>();

        // AccountManager (singleton: 계좌·브로커 런타임 캐시는 앱 전체 공유)
        services.AddSingleton<IAccountManager, AccountManager>();

        return services;
    }
}

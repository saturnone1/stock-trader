using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Extensions;

public static class BrokerServiceExtensions
{
    public static IServiceCollection AddBrokerServices(this IServiceCollection services)
    {
        // HttpClient for LS Securities Broker
        services.AddHttpClient(nameof(LsSecuritiesBrokerService));

        services.AddSingleton<IAccountBrokerServiceFactory, AccountBrokerServiceFactory>();

        // AccountManager (singleton: 계좌·브로커 런타임 캐시는 앱 전체 공유)
        services.AddSingleton<IAccountManager, AccountManager>();

        return services;
    }
}

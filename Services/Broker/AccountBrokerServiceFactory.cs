using StockTrader.Application.Accounts;
using StockTrader.Services.Account;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Services.Broker;

/// <summary>계좌별 자격증명과 중앙 브로커 카탈로그를 런타임 어댑터로 연결합니다.</summary>
public sealed class AccountBrokerServiceFactory(
    IHttpClientFactory httpClientFactory,
    LsAuthService lsAuthService,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
    : IAccountBrokerServiceFactory
{
    public IBrokerService Create(ManagedTradingAccount account)
    {
        _ = BrokerCatalog.Get(account.BrokerType);
        return account.BrokerType switch
        {
            BrokerType.Alpaca => CreateAlpaca(account),
            BrokerType.KoreaInvestment => new KoreaInvestmentBrokerService(
                loggerFactory.CreateLogger<KoreaInvestmentBrokerService>()),
            BrokerType.Kiwoom => new KiwoomBrokerService(
                loggerFactory.CreateLogger<KiwoomBrokerService>()),
            BrokerType.LsSecurities => new LsSecuritiesBrokerService(
                httpClientFactory.CreateClient(nameof(LsSecuritiesBrokerService)),
                lsAuthService,
                timeProvider,
                loggerFactory.CreateLogger<LsSecuritiesBrokerService>()),
            _ => throw new ArgumentOutOfRangeException(
                nameof(account.BrokerType), account.BrokerType, "Unsupported broker type")
        };
    }

    private IBrokerService CreateAlpaca(ManagedTradingAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey)
            || string.IsNullOrWhiteSpace(account.ApiSecret))
        {
            throw new InvalidOperationException(
                $"계좌 [{account.AccountName}]의 API 키가 설정되지 않았습니다. "
                + "계좌 관리 화면에서 API Key와 Secret을 입력해 주세요.");
        }

        var isPaper = !string.Equals(
            account.Environment,
            "Live",
            StringComparison.OrdinalIgnoreCase);
        return new DynamicAlpacaBrokerService(
            account.ApiKey,
            account.ApiSecret,
            isPaper,
            timeProvider,
            loggerFactory.CreateLogger<DynamicAlpacaBrokerService>());
    }
}

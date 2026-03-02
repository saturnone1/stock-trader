using StockTrader.Models.Enums;

namespace StockTrader.Services.Broker;

/// <summary>
/// 런타임에 BrokerType에 따라 적절한 IBrokerService를 반환하는 팩토리.
///
/// DataFeedServiceFactory와 동일한 Keyed DI 패턴을 사용한다.
/// 브로커 전환 시 ServiceCollectionExtensions.cs에서 기본 BrokerType만 변경하면 된다.
/// </summary>
public interface IBrokerServiceFactory
{
    /// <summary>지정된 브로커 타입의 서비스를 반환한다.</summary>
    IBrokerService GetService(BrokerType brokerType);

    /// <summary>현재 설정된 기본 브로커 서비스를 반환한다.</summary>
    IBrokerService GetDefaultService();
}

public class BrokerServiceFactory : IBrokerServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BrokerType _defaultBrokerType;

    public BrokerServiceFactory(
        IServiceProvider serviceProvider,
        BrokerType defaultBrokerType = BrokerType.Alpaca)
    {
        _serviceProvider = serviceProvider;
        _defaultBrokerType = defaultBrokerType;
    }

    /// <inheritdoc />
    public IBrokerService GetService(BrokerType brokerType) => brokerType switch
    {
        BrokerType.Alpaca =>
            _serviceProvider.GetRequiredKeyedService<IBrokerService>(BrokerType.Alpaca),
        BrokerType.KoreaInvestment =>
            _serviceProvider.GetRequiredKeyedService<IBrokerService>(BrokerType.KoreaInvestment),
        BrokerType.Kiwoom =>
            _serviceProvider.GetRequiredKeyedService<IBrokerService>(BrokerType.Kiwoom),
        BrokerType.LsSecurities =>
            _serviceProvider.GetRequiredKeyedService<IBrokerService>(BrokerType.LsSecurities),
        _ => throw new ArgumentOutOfRangeException(nameof(brokerType),
            $"Unsupported broker type: {brokerType}")
    };

    /// <inheritdoc />
    public IBrokerService GetDefaultService() => GetService(_defaultBrokerType);
}

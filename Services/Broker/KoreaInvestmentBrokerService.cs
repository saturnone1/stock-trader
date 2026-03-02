using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>
/// 한국투자증권 (KIS Open API) 브로커 어댑터 — Phase 3.1에서 구현 예정.
///
/// 구현 시 참고:
/// - KIS Open API: https://apiportal.koreainvestment.com
/// - 실시간 시세: WebSocket 기반
/// - 주문 전송: REST API (POST /uapi/domestic-stock/v1/trading/order-cash)
/// - 인증: OAuth2 access_token (6시간 만료)
/// </summary>
public class KoreaInvestmentBrokerService : IBrokerService
{
    private readonly ILogger<KoreaInvestmentBrokerService> _logger;

    public KoreaInvestmentBrokerService(ILogger<KoreaInvestmentBrokerService> logger)
    {
        _logger = logger;
    }

    public Task<bool> PlaceOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] KoreaInvestmentBrokerService is not yet implemented");
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<bool> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }
}

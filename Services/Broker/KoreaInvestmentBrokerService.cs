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

    public Task<BrokerOrder?> SubmitEntryOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] SubmitEntryOrderAsync: 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 주문이 실행되지 않습니다.");
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] CancelOrderAsync: 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
        return Task.FromResult(false);
    }

    public Task<BrokerOrder?> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] ClosePositionAsync({Symbol}): 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.", symbol);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] Position increase is not implemented: {Symbol} {Quantity}",
            symbol, quantity);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] Partial close is not implemented: {Symbol} {Quantity}", symbol, quantity);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] GetPositionsAsync: 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 빈 목록을 반환합니다.");
        return Task.FromResult(new List<Position>());
    }

    public Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] GetAccountAsync: 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. null을 반환합니다.");
        return Task.FromResult<BrokerAccount?>(null);
    }

    public Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[KIS] GetOrderHistoryAsync: 한국투자증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 빈 목록을 반환합니다.");
        return Task.FromResult(new List<BrokerOrder>());
    }
}

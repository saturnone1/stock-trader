using StockTrader.Models;

namespace StockTrader.Services.Broker;

/// <summary>
/// 키움증권 (OpenAPI+) 브로커 어댑터 — Phase 3.1에서 구현 예정.
///
/// 구현 시 참고:
/// - 키움 OpenAPI+: Windows COM 컴포넌트 기반 (KHOpenAPI.ocx)
/// - .NET에서 사용하려면 COM Interop 또는 별도 프로세스 브릿지 필요
/// - 주문: SendOrder() 함수 호출
/// - 실시간: SetRealReg() 이벤트 기반
/// - 주의: Windows 전용, 32bit 제약 있음
/// </summary>
public class KiwoomBrokerService : IBrokerService
{
    private readonly ILogger<KiwoomBrokerService> _logger;

    public KiwoomBrokerService(ILogger<KiwoomBrokerService> logger)
    {
        _logger = logger;
    }

    public Task<bool> PlaceOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] KiwoomBrokerService is not yet implemented");
        throw new NotImplementedException(
            "키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }

    public Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
    }
}

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

    public Task<BrokerOrder?> SubmitEntryOrderAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] SubmitEntryOrderAsync: 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 주문이 실행되지 않습니다.");
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] CancelOrderAsync: 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.");
        return Task.FromResult(false);
    }

    public Task<BrokerOrder?> ClosePositionAsync(string symbol, CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] ClosePositionAsync({Symbol}): 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다.", symbol);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<BrokerOrder?> IncreasePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] Position increase is not implemented: {Symbol} {Quantity}",
            symbol, quantity);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<BrokerOrder?> ClosePositionAsync(
        string symbol,
        int quantity,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] Partial close is not implemented: {Symbol} {Quantity}", symbol, quantity);
        return Task.FromResult<BrokerOrder?>(null);
    }

    public Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] GetPositionsAsync: 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 빈 목록을 반환합니다.");
        return Task.FromResult(new List<Position>());
    }

    public Task<BrokerAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] GetAccountAsync: 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. null을 반환합니다.");
        return Task.FromResult<BrokerAccount?>(null);
    }

    public Task<List<BrokerOrder>> GetOrderHistoryAsync(DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        _logger.LogWarning("[Kiwoom] GetOrderHistoryAsync: 키움증권 브로커 서비스는 Phase 3.1에서 구현 예정입니다. 빈 목록을 반환합니다.");
        return Task.FromResult(new List<BrokerOrder>());
    }
}

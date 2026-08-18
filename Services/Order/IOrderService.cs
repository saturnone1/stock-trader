using StockTrader.Models;

namespace StockTrader.Services.Order;

public interface IOrderService
{
    /// <summary>활성 계좌로 주문을 실행한다.</summary>
    Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, CancellationToken ct = default);

    /// <summary>
    /// 지정된 계좌로 주문을 실행한다.
    /// accountId가 null이면 활성 계좌를 사용한다 (기존 동작 유지).
    /// </summary>
    Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, int? accountId,
        CancellationToken ct = default);

    /// <summary>
    /// 시그널 ID로 수동 주문을 실행한다.
    /// AlertOnly 모드 여부와 관계없이 항상 실제 주문을 제출한다.
    /// </summary>
    /// <param name="signalId">PatternSignal.Id</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>(Success, 결과 메시지)</returns>
    Task<(bool Success, string Message)> PlaceManualOrderAsync(long signalId, CancellationToken ct = default);
}

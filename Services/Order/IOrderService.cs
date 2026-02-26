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

    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default);
}

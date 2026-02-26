using StockTrader.Models;

namespace StockTrader.Services.Order;

public interface IOrderService
{
    Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default);
    Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default);
}

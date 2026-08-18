using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

/// <summary>신규 진입 주문 응답이 요청과 동일한 외부 접수 증거인지 검증합니다.</summary>
public static class LiveEntryOrderEvidencePolicy
{
    public static bool IsTerminalRejection(BrokerOrder order) =>
        order.Status is BrokerOrderStatus.Rejected
            or BrokerOrderStatus.Cancelled
            or BrokerOrderStatus.Expired;

    public static string? ValidateAcceptedOrder(
        TradeRecommendation recommendation,
        BrokerOrder order)
    {
        if (!string.Equals(
                order.Symbol,
                recommendation.Symbol,
                StringComparison.OrdinalIgnoreCase))
            return $"Broker order symbol {order.Symbol} does not match {recommendation.Symbol}.";
        if (order.Direction != TradeDirection.Long)
            return $"Broker order direction {order.Direction} is not a long entry.";
        if (order.Quantity != recommendation.ShareQuantity)
            return $"Broker order quantity {order.Quantity} does not match "
                + $"{recommendation.ShareQuantity}.";
        return null;
    }
}

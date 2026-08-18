using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

public enum PositionOrderReconciliationAction
{
    Wait,
    Finalize,
    ReleaseForRetry,
}

public sealed record PositionOrderReconciliation(
    PositionOrderReconciliationAction Action,
    BrokerOrder? Order = null);

public static class PositionOrderReconciliationPolicy
{
    public static PositionOrderReconciliation Resolve(
        string symbol,
        string? orderId,
        DateTime requestedAt,
        TradeDirection expectedDirection,
        IReadOnlyCollection<BrokerOrder> orders)
    {
        var candidates = orders.Where(order =>
                order.Direction == expectedDirection
                && order.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && order.SubmittedAt >= requestedAt.AddSeconds(-2));
        if (!string.IsNullOrWhiteSpace(orderId))
            candidates = candidates.Where(order => order.OrderId == orderId);

        var order = candidates
            .OrderByDescending(item => item.FilledAt ?? item.SubmittedAt)
            .FirstOrDefault();
        if (order is null)
            return new PositionOrderReconciliation(PositionOrderReconciliationAction.Wait);

        return order.Status switch
        {
            BrokerOrderStatus.Filled when order.AverageFillPrice is > 0
                => new PositionOrderReconciliation(PositionOrderReconciliationAction.Finalize, order),
            BrokerOrderStatus.Cancelled or BrokerOrderStatus.Rejected or BrokerOrderStatus.Expired
                => new PositionOrderReconciliation(
                    PositionOrderReconciliationAction.ReleaseForRetry, order),
            _ => new PositionOrderReconciliation(PositionOrderReconciliationAction.Wait, order),
        };
    }
}

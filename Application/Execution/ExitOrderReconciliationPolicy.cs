using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

public enum ExitOrderReconciliationAction
{
    Wait,
    Finalize,
    ReleaseForRetry,
}

public sealed record ExitOrderReconciliation(
    ExitOrderReconciliationAction Action,
    BrokerOrder? Order = null);

public static class ExitOrderReconciliationPolicy
{
    public static ExitOrderReconciliation Resolve(
        string symbol,
        string? orderId,
        DateTime requestedAt,
        IReadOnlyCollection<BrokerOrder> orders)
    {
        var candidates = orders.Where(order =>
                order.Direction == TradeDirection.Short
                && order.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                && order.SubmittedAt >= requestedAt.AddSeconds(-2));
        if (!string.IsNullOrWhiteSpace(orderId))
            candidates = candidates.Where(order => order.OrderId == orderId);

        var order = candidates
            .OrderByDescending(item => item.FilledAt ?? item.SubmittedAt)
            .FirstOrDefault();
        if (order is null)
            return new ExitOrderReconciliation(ExitOrderReconciliationAction.Wait);

        return order.Status switch
        {
            BrokerOrderStatus.Filled when order.AverageFillPrice is > 0
                => new ExitOrderReconciliation(ExitOrderReconciliationAction.Finalize, order),
            BrokerOrderStatus.Cancelled or BrokerOrderStatus.Rejected or BrokerOrderStatus.Expired
                => new ExitOrderReconciliation(ExitOrderReconciliationAction.ReleaseForRetry, order),
            _ => new ExitOrderReconciliation(ExitOrderReconciliationAction.Wait, order),
        };
    }
}

using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

public enum EntryOrderReconciliationAction
{
    Wait,
    Finalize,
    ReleaseForRetry,
    EvidenceMismatch,
    Ambiguous,
}

public sealed record EntryOrderReconciliation(
    EntryOrderReconciliationAction Action,
    BrokerOrder? Order = null);

/// <summary>
/// 신규 진입 의도와 브로커 주문 내역을 보수적으로 연결한다. 주문 ID가 없을 때는
/// 정확히 하나의 주문만 일치해야 하며, 모호한 경우 자동 체결 반영이나 재시도를 하지 않는다.
/// </summary>
public static class EntryOrderReconciliationPolicy
{
    public static EntryOrderReconciliation Resolve(
        TradeRecommendation recommendation,
        IReadOnlyCollection<BrokerOrder> orders)
    {
        if (!recommendation.EntryRequestedAt.HasValue)
            return new EntryOrderReconciliation(EntryOrderReconciliationAction.Wait);

        var requestedAt = recommendation.EntryRequestedAt.Value;
        if (!string.IsNullOrWhiteSpace(recommendation.EntryOrderId))
        {
            var byId = orders
                .Where(order => order.OrderId == recommendation.EntryOrderId)
                .ToArray();
            if (byId.Length == 0)
                return new EntryOrderReconciliation(EntryOrderReconciliationAction.Wait);
            if (byId.Length > 1)
                return new EntryOrderReconciliation(EntryOrderReconciliationAction.Ambiguous);
            if (LiveEntryOrderEvidencePolicy.ValidateAcceptedOrder(
                    recommendation, byId[0]) is not null)
            {
                return new EntryOrderReconciliation(
                    EntryOrderReconciliationAction.EvidenceMismatch,
                    byId[0]);
            }

            return FromStatus(byId[0]);
        }

        var candidates = orders
            .Where(order => order.Direction == TradeDirection.Long
                && order.Symbol.Equals(
                    recommendation.Symbol,
                    StringComparison.OrdinalIgnoreCase)
                && order.Quantity == recommendation.ShareQuantity
                && order.SubmittedAt >= requestedAt.AddSeconds(-2))
            .OrderByDescending(order => order.SubmittedAt)
            .ToArray();
        return candidates.Length switch
        {
            0 => new EntryOrderReconciliation(EntryOrderReconciliationAction.Wait),
            > 1 => new EntryOrderReconciliation(EntryOrderReconciliationAction.Ambiguous),
            _ => FromStatus(candidates[0]),
        };
    }

    private static EntryOrderReconciliation FromStatus(BrokerOrder order) => order.Status switch
    {
        BrokerOrderStatus.Filled when order.AverageFillPrice is > 0
            => new EntryOrderReconciliation(EntryOrderReconciliationAction.Finalize, order),
        BrokerOrderStatus.Cancelled or BrokerOrderStatus.Rejected or BrokerOrderStatus.Expired
            => new EntryOrderReconciliation(
                EntryOrderReconciliationAction.ReleaseForRetry,
                order),
        _ => new EntryOrderReconciliation(EntryOrderReconciliationAction.Wait, order),
    };
}

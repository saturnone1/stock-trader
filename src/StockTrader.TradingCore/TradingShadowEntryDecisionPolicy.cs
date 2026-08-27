using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public sealed record TradingShadowEntryDecisionRequest(
    string OrderMode,
    TradingEntryIntent Intent,
    bool IsMarketOpen,
    TradingRiskProjection Risk,
    IReadOnlyList<TradingPositionProjection> Positions,
    TradingAccountConfigurationSet AccountConfiguration);

public sealed record TradingShadowEntryDecision(string Disposition, string? Reason);

/// <summary>
/// Candidate admission decision over immutable command, projection, account, and session evidence.
/// It deliberately has no broker or persistence dependency.
/// </summary>
public static class TradingShadowEntryDecisionPolicy
{
    public static TradingShadowEntryDecision Evaluate(TradingShadowEntryDecisionRequest request)
    {
        if (string.Equals(request.OrderMode, "AlertOnly", StringComparison.Ordinal))
            return new(TradingShadowDispositions.RecommendationOnly, null);
        if (!string.Equals(request.OrderMode, "AutoOrder", StringComparison.Ordinal))
            return Block("unsupported-order-mode");
        if (!request.IsMarketOpen)
            return Block("market-closed");
        if (request.Risk.IsTradingHalted)
            return Block("risk-trading-halted");
        var account = request.AccountConfiguration.Accounts.SingleOrDefault(value =>
            string.Equals(value.AccountId, request.Intent.AccountId, StringComparison.Ordinal));
        if (account is null || !account.IsEnabled || !account.IsActive)
            return Block("active-trading-account-missing");
        var open = request.Positions.Where(value =>
            !value.ClosedAtUtc.HasValue && value.Quantity > 0).ToArray();
        if (open.Any(value => value.Symbol.Equals(
                request.Intent.Symbol, StringComparison.OrdinalIgnoreCase)))
            return Block("symbol-position-already-open");
        if (open.Length >= request.AccountConfiguration.Risk.MaxTotalPositions)
            return Block("maximum-position-count-reached");
        if (!string.IsNullOrWhiteSpace(request.Intent.Sector)
            && open.Count(value => value.Sector.Equals(
                request.Intent.Sector, StringComparison.OrdinalIgnoreCase))
                >= request.AccountConfiguration.Risk.MaxPositionsPerSector)
            return Block("maximum-sector-position-count-reached");
        return new(TradingShadowDispositions.BrokerSubmission, null);
    }

    private static TradingShadowEntryDecision Block(string reason) =>
        new(TradingShadowDispositions.Blocked, reason);
}

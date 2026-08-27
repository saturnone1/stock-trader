using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.Broker;

namespace StockTrader.TradingCore.Execution;

public static class TradingPortfolioProjectionPolicy
{
    public static TradingPositionProjection ApplyBrokerMarket(
        TradingPositionProjection position,
        BrokerPositionEvidence evidence)
    {
        if (!position.Symbol.Equals(evidence.Symbol, StringComparison.OrdinalIgnoreCase)
            || evidence.Quantity < 0
            || evidence.CurrentPrice <= 0)
            throw new ArgumentException("Broker position evidence does not match canonical position.", nameof(evidence));
        return position with
        {
            CurrentPrice = evidence.CurrentPrice,
            HighSinceEntry = Math.Max(position.HighSinceEntry, evidence.CurrentPrice),
            ExecutionContext = position.ExecutionContext,
        };
    }

    public static TradingRiskProjection Risk(
        IReadOnlyCollection<BrokerAccountEvidence> accounts,
        int openPositionCount,
        decimal dailyLossLimitPercent,
        bool hasPortfolioDivergence,
        DateTime observedAtUtc)
    {
        var equity = accounts.Sum(value => value.TotalEquity);
        var previous = accounts.Sum(value => value.PreviousDayEquity);
        var pnl = equity - previous;
        var pnlPercent = previous > 0 ? pnl / previous : 0m;
        return new TradingRiskProjection(
            pnl,
            pnlPercent,
            openPositionCount,
            hasPortfolioDivergence
                || accounts.Any(value => value.IsTradingBlocked)
                || previous <= 0
                || pnlPercent <= -dailyLossLimitPercent,
            observedAtUtc.Kind == DateTimeKind.Utc
                ? observedAtUtc
                : observedAtUtc.ToUniversalTime());
    }
}

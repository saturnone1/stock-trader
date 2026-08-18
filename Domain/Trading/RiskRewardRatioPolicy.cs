namespace StockTrader.Domain.Trading;

public static class RiskRewardRatioPolicy
{
    public static decimal CalculateLong(
        decimal entryPrice,
        decimal stopLossPrice,
        decimal targetPrice) =>
        entryPrice > 0m
        && stopLossPrice > 0m
        && stopLossPrice < entryPrice
            ? (targetPrice - entryPrice) / (entryPrice - stopLossPrice)
            : 0m;

    public static decimal CalculateWithAbsoluteStopDistance(
        decimal entryPrice,
        decimal stopLossPrice,
        decimal targetPrice)
    {
        if (entryPrice == 0m)
            return 0m;
        var stopDistance = Math.Abs(entryPrice - stopLossPrice);
        return stopDistance == 0m
            ? 0m
            : (targetPrice - entryPrice) / stopDistance;
    }
}

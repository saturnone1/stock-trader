namespace StockTrader.Domain.Statistics;

public static class PatternStatisticsMetricPolicy
{
    public static decimal CalculateExpectancy(
        decimal winRate,
        decimal averageWinPercent,
        decimal averageLossPercent) =>
        winRate * averageWinPercent
        - (1m - winRate) * averageLossPercent;

    public static decimal CalculateProfitFactor(
        decimal winRate,
        decimal averageWinPercent,
        decimal averageLossPercent)
    {
        var grossLoss = (1m - winRate) * averageLossPercent;
        return grossLoss > 0m
            ? (winRate * averageWinPercent) / grossLoss
            : 0m;
    }
}

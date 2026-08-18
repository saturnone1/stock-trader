using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Statistics;

public static class PatternStatisticsSelectionPolicy
{
    public static PatternStatisticsSnapshot? Resolve(
        PatternType patternType,
        string symbol,
        IReadOnlyList<PatternStatisticsSnapshot> statistics) =>
        statistics
            .Where(statistic => statistic.PatternType == patternType)
            .OrderByDescending(statistic => string.Equals(
                statistic.Symbol,
                symbol,
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(statistic => string.IsNullOrWhiteSpace(statistic.Symbol))
            .ThenByDescending(statistic => statistic.LastUpdated)
            .FirstOrDefault(statistic =>
                string.IsNullOrWhiteSpace(statistic.Symbol)
                || string.Equals(
                    statistic.Symbol,
                    symbol,
                    StringComparison.OrdinalIgnoreCase));
}

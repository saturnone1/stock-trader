using StockTrader.Domain.Strategies;
using StockTrader.Domain.Statistics;

namespace StockTrader.Application.Statistics;

public sealed record PatternStatisticsSnapshot(
    PatternType PatternType,
    string? Symbol,
    int SampleSize,
    decimal WinRate,
    decimal AvgWinPercent,
    decimal AvgLossPercent,
    decimal MaxDrawdownPercent,
    DateTime LastUpdated)
{
    public decimal Expectancy => PatternStatisticsMetricPolicy.CalculateExpectancy(
        WinRate,
        AvgWinPercent,
        AvgLossPercent);

    public decimal ProfitFactor => PatternStatisticsMetricPolicy.CalculateProfitFactor(
        WinRate,
        AvgWinPercent,
        AvgLossPercent);
}

public interface IPatternStatisticsQuery
{
    Task<IReadOnlyList<PatternStatisticsSnapshot>> GetAllAsync(
        CancellationToken ct = default);

    Task<IReadOnlyList<PatternStatisticsSnapshot>> GetByExpectancyAsync(
        CancellationToken ct = default);
}

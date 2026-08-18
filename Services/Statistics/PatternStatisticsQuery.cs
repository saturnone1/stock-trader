using StockTrader.Application.Statistics;

namespace StockTrader.Services.Statistics;

public sealed class PatternStatisticsQuery(IStatisticsService statistics)
    : IPatternStatisticsQuery
{
    public async Task<IReadOnlyList<PatternStatisticsSnapshot>> GetAllAsync(
        CancellationToken ct = default) =>
        (await statistics.GetAllStatsAsync(ct))
            .Select(stat => new PatternStatisticsSnapshot(
                stat.PatternType,
                stat.Symbol,
                stat.SampleSize,
                stat.WinRate,
                stat.AvgWinPercent,
                stat.AvgLossPercent,
                stat.MaxDrawdownPercent,
                stat.LastUpdated))
            .ToArray();

    public async Task<IReadOnlyList<PatternStatisticsSnapshot>> GetByExpectancyAsync(
        CancellationToken ct = default) =>
        (await GetAllAsync(ct))
            .OrderByDescending(stat => stat.Expectancy)
            .ThenBy(stat => stat.PatternType)
            .ThenBy(stat => stat.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

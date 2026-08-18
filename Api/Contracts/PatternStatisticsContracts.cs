using StockTrader.Application.Statistics;

namespace StockTrader.Api.Contracts;

public sealed record PatternStatisticsResponse(
    string Pattern,
    string? Symbol,
    int SampleSize,
    decimal WinRate,
    decimal AvgWinPercent,
    decimal AvgLossPercent,
    decimal MaxDrawdownPercent,
    decimal Expectancy,
    decimal ProfitFactor,
    string LastUpdated);

public sealed record PatternStatisticsListResponse(
    int Count,
    IReadOnlyList<PatternStatisticsResponse> Stats);

public static class PatternStatisticsResponseMapper
{
    public static PatternStatisticsResponse Map(PatternStatisticsSnapshot stat) => new(
        stat.PatternType.ToString(),
        stat.Symbol,
        stat.SampleSize,
        stat.WinRate,
        stat.AvgWinPercent,
        stat.AvgLossPercent,
        stat.MaxDrawdownPercent,
        stat.Expectancy,
        stat.ProfitFactor,
        stat.LastUpdated.ToString("o"));
}

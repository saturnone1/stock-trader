using StockTrader.Application.Portfolio;

namespace StockTrader.Api.Contracts;

public sealed record PortfolioPatternStatisticsResponse(
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

public sealed record PortfolioEquityPointResponse(
    string Date,
    string Symbol,
    string Pattern,
    decimal PnL,
    decimal PnLPercent,
    decimal CumulativePnL);

public sealed record PortfolioPerformanceResponse(
    int TotalTrades,
    decimal WinRate,
    decimal AvgWinPercent,
    decimal AvgLossPercent,
    decimal MaxDrawdown,
    IReadOnlyList<PortfolioPatternStatisticsResponse> PatternStats,
    IReadOnlyList<PortfolioEquityPointResponse> EquityCurve)
{
    public static PortfolioPerformanceResponse Create(PortfolioPerformanceSnapshot snapshot) => new(
        snapshot.TotalTrades,
        snapshot.WinRate,
        snapshot.AvgWinPercent,
        snapshot.AvgLossPercent,
        snapshot.MaxDrawdown,
        snapshot.PatternStats.Select(stat => new PortfolioPatternStatisticsResponse(
            stat.Pattern,
            stat.Symbol,
            stat.SampleSize,
            stat.WinRate,
            stat.AvgWinPercent,
            stat.AvgLossPercent,
            stat.MaxDrawdownPercent,
            stat.Expectancy,
            stat.ProfitFactor,
            stat.LastUpdated.ToString("o"))).ToArray(),
        snapshot.EquityCurve.Select(point => new PortfolioEquityPointResponse(
            point.ExitTime.ToString("yyyy-MM-dd"),
            point.Symbol,
            point.Pattern,
            point.PnL,
            point.PnLPercent,
            point.CumulativePnL)).ToArray());
}

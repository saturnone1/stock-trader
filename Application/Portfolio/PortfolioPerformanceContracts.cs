using StockTrader.Application.Statistics;

namespace StockTrader.Application.Portfolio;

public sealed record PortfolioCompletedTrade(
    long Id,
    string Symbol,
    string Pattern,
    DateTime ExitTime,
    decimal PnL,
    decimal PnLPercent,
    bool IsWin);

public sealed record PortfolioEquityPoint(
    DateTime ExitTime,
    string Symbol,
    string Pattern,
    decimal PnL,
    decimal PnLPercent,
    decimal CumulativePnL);

public sealed record PortfolioPerformanceSnapshot(
    int TotalTrades,
    decimal WinRate,
    decimal AvgWinPercent,
    decimal AvgLossPercent,
    decimal MaxDrawdown,
    IReadOnlyList<PatternStatisticsSnapshot> PatternStats,
    IReadOnlyList<PortfolioEquityPoint> EquityCurve);

public interface IPortfolioPerformanceQuery
{
    Task<PortfolioPerformanceSnapshot> GetAsync(CancellationToken ct = default);
}

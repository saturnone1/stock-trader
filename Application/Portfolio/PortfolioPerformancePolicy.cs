using StockTrader.Application.Statistics;

namespace StockTrader.Application.Portfolio;

/// <summary>
/// 완료 거래로부터 포트폴리오 성과를 결정적으로 계산합니다.
/// 최대낙폭은 현재 설정된 초기 계좌자산에 실현손익을 순서대로 반영한 자산곡선을 기준으로 합니다.
/// </summary>
public static class PortfolioPerformancePolicy
{
    public static PortfolioPerformanceSnapshot Evaluate(
        IEnumerable<PortfolioCompletedTrade> source,
        decimal initialAccountEquity,
        IReadOnlyList<PatternStatisticsSnapshot> patternStats)
    {
        if (initialAccountEquity <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(initialAccountEquity),
                "Initial account equity must be positive.");

        var trades = source
            .OrderBy(trade => trade.ExitTime)
            .ThenBy(trade => trade.Id)
            .ToArray();
        var wins = trades.Where(trade => trade.IsWin).ToArray();
        var losses = trades.Where(trade => !trade.IsWin).ToArray();
        var equityCurve = new List<PortfolioEquityPoint>(trades.Length);
        var equity = initialAccountEquity;
        var peakEquity = initialAccountEquity;
        var cumulativePnL = 0m;
        var maxDrawdown = 0m;

        foreach (var trade in trades)
        {
            cumulativePnL += trade.PnL;
            equity += trade.PnL;
            peakEquity = Math.Max(peakEquity, equity);
            var drawdown = peakEquity > 0m
                ? (peakEquity - equity) / peakEquity
                : 0m;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);
            equityCurve.Add(new PortfolioEquityPoint(
                trade.ExitTime,
                trade.Symbol,
                trade.Pattern,
                trade.PnL,
                trade.PnLPercent,
                cumulativePnL));
        }

        return new PortfolioPerformanceSnapshot(
            trades.Length,
            trades.Length > 0 ? (decimal)wins.Length / trades.Length : 0m,
            wins.Length > 0 ? wins.Average(trade => trade.PnLPercent) : 0m,
            losses.Length > 0 ? losses.Average(trade => trade.PnLPercent) : 0m,
            maxDrawdown,
            patternStats,
            equityCurve);
    }
}

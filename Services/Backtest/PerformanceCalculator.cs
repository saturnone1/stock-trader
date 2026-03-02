using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트 성과 지표 계산: 패턴별 통계, 샤프 비율, 그룹 낙폭.
/// 모든 메서드는 순수 함수(stateless)입니다.
/// </summary>
internal static class PerformanceCalculator
{
    public static Dictionary<PatternType, PatternStats> ComputePerPatternStats(
        List<TradeRecord> trades)
    {
        var stats = new Dictionary<PatternType, PatternStats>();

        foreach (var group in trades.GroupBy(t => t.PatternType))
        {
            var all = group.ToList();
            int winCount = 0, lossCount = 0;
            decimal winPnlSum = 0, lossPnlSum = 0;

            foreach (var t in all)
            {
                if (t.IsWin)
                {
                    winCount++;
                    winPnlSum += t.PnLPercent;
                }
                else
                {
                    lossCount++;
                    lossPnlSum += t.PnLPercent;
                }
            }

            stats[group.Key] = new PatternStats
            {
                PatternType    = group.Key,
                SampleSize     = all.Count,
                WinRate        = all.Count > 0 ? (decimal)winCount / all.Count : 0,
                AvgWinPercent  = winCount  > 0 ? winPnlSum  / winCount  : 0,
                AvgLossPercent = lossCount > 0 ? Math.Abs(lossPnlSum / lossCount) : 0,
                MaxDrawdownPercent = ComputeGroupDrawdown(all),
                LastUpdated    = DateTime.UtcNow
            };
        }

        return stats;
    }

    /// <summary>
    /// 연율화 샤프 비율. 거래 PnL%의 mean/stdDev에 sqrt(연간 추정 거래 횟수)를 곱한다.
    /// 거래 기간(첫 진입~마지막 청산)을 기준으로 연간 거래 횟수를 추정.
    /// </summary>
    public static decimal ComputeSharpeRatio(
        List<TradeRecord> trades,
        TimeFrame timeFrame = TimeFrame.Daily)
    {
        if (trades.Count < 2) return 0;

        int n = trades.Count;
        decimal sum = 0;
        for (int i = 0; i < n; i++) sum += trades[i].PnLPercent;
        var avgReturn = sum / n;

        decimal sumSqDiff = 0;
        for (int i = 0; i < n; i++)
        {
            var d = trades[i].PnLPercent - avgReturn;
            sumSqDiff += d * d;
        }
        var variance = sumSqDiff / n;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        if (stdDev <= 0) return 0;

        // 거래 기간에서 연간 거래 횟수 추정
        var firstEntry = trades.Min(t => t.EntryTime);
        var lastExit = trades.Max(t => t.ExitTime != default ? t.ExitTime : t.EntryTime);
        var tradingDays = Math.Max(1.0, (lastExit - firstEntry).TotalDays);
        var tradesPerYear = n / tradingDays * 365.25;

        return avgReturn / stdDev * (decimal)Math.Sqrt(tradesPerYear);
    }

    public static List<SymbolStats> ComputePerSymbolStats(
        List<TradeRecord> trades, decimal initialCapital)
    {
        return trades
            .GroupBy(t => t.Symbol)
            .Select(g =>
            {
                var all = g.ToList();
                var wins = all.Count(t => t.IsWin);
                var totalPnl = all.Sum(t => t.PnL);
                var maxPosSize = all.Max(t => t.EntryPrice * t.Quantity);
                return new SymbolStats
                {
                    Symbol = g.Key,
                    TradeCount = all.Count,
                    WinRate = all.Count > 0 ? (decimal)wins / all.Count : 0,
                    TotalPnL = totalPnl,
                    AvgPnLPercent = all.Count > 0 ? all.Average(t => t.PnLPercent) : 0,
                    MaxPositionSize = maxPosSize,
                    MaxAllocationPercent = initialCapital > 0 ? maxPosSize / initialCapital : 0
                };
            })
            .OrderByDescending(s => s.TotalPnL)
            .ToList();
    }

    public static decimal ComputeGroupDrawdown(List<TradeRecord> trades)
    {
        var cumPnl = 0m;
        var peak = 0m;
        var maxDd = 0m;

        foreach (var t in trades.OrderBy(t => t.EntryTime))
        {
            cumPnl += t.PnLPercent;
            if (cumPnl > peak) peak = cumPnl;
            var dd = peak - cumPnl;
            if (dd > maxDd) maxDd = dd;
        }

        return maxDd;
    }
}

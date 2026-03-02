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

        var annualizationFactor = timeFrame switch
        {
            TimeFrame.OneMinute     => 252.0 * 390.0,
            TimeFrame.FiveMinute    => 252.0 * 78.0,
            TimeFrame.FifteenMinute => 252.0 * 26.0,
            TimeFrame.Daily         => 252.0,
            TimeFrame.Weekly        => 52.0,
            _                       => 252.0
        };

        return avgReturn / stdDev * (decimal)Math.Sqrt(annualizationFactor / Math.Max(1, trades.Count));
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

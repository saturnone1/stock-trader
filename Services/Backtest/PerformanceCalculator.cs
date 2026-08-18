using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 백테스트 성과 지표 계산: 패턴별 통계, 샤프 비율, 그룹 낙폭.
/// 모든 메서드는 순수 함수(stateless)입니다.
/// </summary>
internal static class PerformanceCalculator
{
    // ── [B-1] 고급 성과 지표 ──────────────────────────────────────────────

    /// <summary>
    /// Profit Factor: 총 수익 / 총 손실. 1.5이상 양호, 2이상 우수.
    /// </summary>
    public static decimal ComputeProfitFactor(List<TradeRecord> trades)
    {
        var wins   = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var losses = Math.Abs(trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
        return losses > 0 ? wins / losses : wins > 0 ? 99.9m : 0;
    }

    // ── [E-1] Kelly Criterion ─────────────────────────────────────────────

    /// <summary>
    /// Kelly Criterion: f* = p - q/b
    /// 최대 25%로 상한 제한 (Half-Kelly 권장).
    /// </summary>
    public static decimal ComputeKellyFraction(decimal winRate, decimal avgWinPct, decimal avgLossPct)
        => LongPositionSizingPolicy.ComputeKellyFraction(winRate, avgWinPct, avgLossPct);

    // ── [B-3] MAE/MFE 통계 ────────────────────────────────────────────────

    /// <summary>
    /// MAE(Maximum Adverse Excursion) / MFE(Maximum Favorable Excursion) 통계.
    /// MaePercent는 음수(불리한 방향), MfePercent는 양수(유리한 방향).
    /// </summary>
    public static (decimal avgMae, decimal avgMfe, decimal medianMae, decimal medianMfe) ComputeMaeMfe(
        List<TradeRecord> trades)
    {
        var maes = trades.Where(t => t.MaePercent != 0).Select(t => t.MaePercent).ToList();
        var mfes = trades.Where(t => t.MfePercent != 0).Select(t => t.MfePercent).ToList();

        if (maes.Count == 0 && mfes.Count == 0)
            return (0, 0, 0, 0);

        var avgMae = maes.Count > 0 ? maes.Average() : 0;
        var avgMfe = mfes.Count > 0 ? mfes.Average() : 0;

        var medianMae = ComputeMedian(maes);
        var medianMfe = ComputeMedian(mfes);

        return (avgMae, avgMfe, medianMae, medianMfe);
    }

    // ── [F-1] 레짐별 성과 분해 ────────────────────────────────────────────

    /// <summary>
    /// 레짐별 (Bull/Bear) + 연도별 성과 분해.
    /// spyAbove200Ma: 날짜 → SPY가 200MA 위에 있는지. null이면 Bull로 처리.
    /// </summary>
    public static Dictionary<string, RegimePerformance> ComputeRegimeStats(
        List<TradeRecord> trades, Dictionary<DateTime, bool>? spyAbove200Ma)
    {
        var result = new Dictionary<string, RegimePerformance>();
        if (trades.Count == 0) return result;

        // 레짐 분류 + 연도별 분류
        var groups = new Dictionary<string, List<TradeRecord>>();

        foreach (var trade in trades)
        {
            // Bull/Bear 레짐
            bool isBull = true;
            if (spyAbove200Ma != null)
            {
                var entryDate = trade.EntryTime.Date;
                // 정확한 날짜 없으면 가장 가까운 이전 날짜로 fallback
                if (!spyAbove200Ma.TryGetValue(entryDate, out isBull))
                {
                    var closestKey = spyAbove200Ma.Keys
                        .Where(d => d <= entryDate)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();
                    isBull = closestKey != default ? spyAbove200Ma[closestKey] : true;
                }
            }

            var regimeKey = isBull ? "Bull" : "Bear";
            if (!groups.ContainsKey(regimeKey)) groups[regimeKey] = new List<TradeRecord>();
            groups[regimeKey].Add(trade);

            // 연도별
            var yearKey = trade.EntryTime.Year.ToString();
            if (!groups.ContainsKey(yearKey)) groups[yearKey] = new List<TradeRecord>();
            groups[yearKey].Add(trade);
        }

        foreach (var (key, groupTrades) in groups)
        {
            if (groupTrades.Count == 0) continue;

            var winCount = groupTrades.Count(t => t.IsWin);
            var winRate = (decimal)winCount / groupTrades.Count;
            var totalReturn = groupTrades.Sum(t => t.PnLPercent);
            var avgReturn = totalReturn / groupTrades.Count;
            var maxDd = ComputeGroupDrawdown(groupTrades);

            // 그룹 내 샤프 (간소화: 단일 패스)
            decimal sharpe = 0;
            if (groupTrades.Count >= 2)
            {
                int n = groupTrades.Count;
                decimal sum = 0;
                for (int i = 0; i < n; i++) sum += groupTrades[i].PnLPercent;
                var avg = sum / n;
                decimal sqDiff = 0;
                for (int i = 0; i < n; i++) { var d = groupTrades[i].PnLPercent - avg; sqDiff += d * d; }
                var stdDev = (decimal)Math.Sqrt((double)(sqDiff / (n - 1)));
                if (stdDev > 0) sharpe = avg / stdDev
                    * (decimal)Math.Sqrt(EstimateTradesPerYear(groupTrades));
            }

            result[key] = new RegimePerformance
            {
                TradeCount       = groupTrades.Count,
                WinRate          = winRate,
                TotalReturn      = totalReturn,
                AvgReturnPercent = avgReturn,
                SharpeRatio      = sharpe,
                MaxDrawdown      = maxDd
            };
        }

        return result;
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────────────

    private static decimal ComputeMedian(List<decimal> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }


    public static Dictionary<PatternType, PatternStats> ComputePerPatternStats(
        List<TradeRecord> trades,
        DateTime calculatedAt)
    {
        var stats = new Dictionary<PatternType, PatternStats>();

        foreach (var group in trades.GroupBy(t => t.PatternType))
        {
            stats[group.Key] = ComputeStats(group.ToList(), group.Key, calculatedAt);
        }

        return stats;
    }

    public static Dictionary<string, PatternStats> ComputePerStrategyStats(
        List<TradeRecord> trades,
        DateTime calculatedAt)
    {
        return trades
            .GroupBy(trade => string.IsNullOrWhiteSpace(trade.CustomPatternName)
                ? trade.PatternType.ToString()
                : trade.CustomPatternName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ComputeStats(
                    group.ToList(),
                    group.First().PatternType,
                    calculatedAt),
                StringComparer.OrdinalIgnoreCase);
    }

    private static PatternStats ComputeStats(
        List<TradeRecord> trades,
        PatternType patternType,
        DateTime calculatedAt)
    {
        var wins = trades.Where(trade => trade.IsWin).ToList();
        var losses = trades.Where(trade => !trade.IsWin).ToList();
        return new PatternStats
        {
            PatternType = patternType,
            SampleSize = trades.Count,
            WinRate = trades.Count > 0 ? (decimal)wins.Count / trades.Count : 0,
            AvgWinPercent = wins.Count > 0 ? wins.Average(trade => trade.PnLPercent) : 0,
            AvgLossPercent = losses.Count > 0 ? Math.Abs(losses.Average(trade => trade.PnLPercent)) : 0,
            MaxDrawdownPercent = ComputeGroupDrawdown(trades),
            LastUpdated = calculatedAt
        };
    }

    public static List<SymbolStats> ComputePerSymbolStats(
        List<TradeRecord> trades, decimal initialCapital)
    {
        // 단일 패스로 모든 집계값 계산 (LINQ 다중 순회 제거)
        var groups = new Dictionary<string, (int count, int wins, decimal pnlSum, decimal pnlPctSum, decimal maxPosSize)>();

        foreach (var t in trades)
        {
            var posSize = t.EntryPrice * t.Quantity;
            if (groups.TryGetValue(t.Symbol, out var g))
            {
                groups[t.Symbol] = (
                    g.count + 1,
                    g.wins + (t.IsWin ? 1 : 0),
                    g.pnlSum + t.PnL,
                    g.pnlPctSum + t.PnLPercent,
                    Math.Max(g.maxPosSize, posSize)
                );
            }
            else
            {
                groups[t.Symbol] = (1, t.IsWin ? 1 : 0, t.PnL, t.PnLPercent, posSize);
            }
        }

        var result = new List<SymbolStats>(groups.Count);
        foreach (var (symbol, g) in groups)
        {
            result.Add(new SymbolStats
            {
                Symbol = symbol,
                TradeCount = g.count,
                WinRate = g.count > 0 ? (decimal)g.wins / g.count : 0,
                TotalPnL = g.pnlSum,
                AvgPnLPercent = g.count > 0 ? g.pnlPctSum / g.count : 0,
                MaxPositionSize = g.maxPosSize,
                MaxAllocationPercent = initialCapital > 0 ? g.maxPosSize / initialCapital : 0
            });
        }

        result.Sort((a, b) => b.TotalPnL.CompareTo(a.TotalPnL));
        return result;
    }

    public static decimal ComputeGroupDrawdown(List<TradeRecord> trades)
    {
        var equity = 1m;
        var peak = 1m;
        var maxDd = 0m;

        foreach (var t in trades.OrderBy(t => t.ExitTime))
        {
            equity *= Math.Max(0m, 1m + t.PnLPercent);
            if (equity > peak) peak = equity;
            var dd = peak > 0 ? (peak - equity) / peak : 0m;
            if (dd > maxDd) maxDd = dd;
        }

        return maxDd;
    }

    internal static List<TradeRecord> AggregateTradeCycles(IEnumerable<TradeRecord> executions)
    {
        return executions
            .GroupBy(trade => new
            {
                trade.Symbol,
                trade.PatternType,
                Strategy = trade.CustomPatternName ?? string.Empty,
                trade.EntryTime
            })
            .Select(group =>
            {
                var quantity = group.Sum(trade => trade.Quantity);
                var entryNotional = group.Sum(trade => trade.EntryPrice * trade.Quantity);
                var pnl = group.Sum(trade => trade.PnL);
                var first = group.First();
                return new TradeRecord
                {
                    Symbol = first.Symbol,
                    PatternType = first.PatternType,
                    CustomPatternName = first.CustomPatternName,
                    EntryPrice = quantity > 0 ? entryNotional / quantity : first.EntryPrice,
                    ExitPrice = group.OrderBy(trade => trade.ExitTime).Last().ExitPrice,
                    Quantity = quantity,
                    EntryTime = first.EntryTime,
                    ExitTime = group.Max(trade => trade.ExitTime),
                    PnL = pnl,
                    PnLPercent = entryNotional > 0 ? pnl / entryNotional : 0,
                    ExitReason = string.Join(" + ", group.Select(trade => trade.ExitReason).Distinct()),
                    EntryAtr = first.EntryAtr,
                    EntryVolume = first.EntryVolume,
                    EquityAtEntry = first.EquityAtEntry,
                    MaePercent = group.Min(trade => trade.MaePercent),
                    MfePercent = group.Max(trade => trade.MfePercent)
                };
            })
            .OrderBy(trade => trade.EntryTime)
            .ToList();
    }

    private static double EstimateTradesPerYear(List<TradeRecord> trades)
    {
        var firstEntry = trades.Min(trade => trade.EntryTime);
        var lastExit = trades.Max(trade => trade.ExitTime != default ? trade.ExitTime : trade.EntryTime);
        var calendarDays = Math.Max(1.0, (lastExit - firstEntry).TotalDays);
        return Math.Max(1.0, trades.Count / calendarDays * 365.25);
    }
}

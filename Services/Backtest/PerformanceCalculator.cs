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
    /// Sortino 비율: 하방 변동성(음수 수익만)으로 연율화.
    /// Sharpe 비율보다 하방 리스크를 민감하게 반영합니다.
    /// </summary>
    public static decimal ComputeSortinoRatio(List<TradeRecord> trades, TimeFrame tf)
    {
        if (trades.Count < 2) return 0;
        var returns = trades.Select(t => t.PnLPercent / 100m).ToList();
        var mean = returns.Average();
        var downReturns = returns.Where(r => r < 0).ToList();
        if (downReturns.Count == 0) return mean > 0 ? 10m : 0;
        var downDev = (decimal)Math.Sqrt((double)downReturns.Average(r => r * r));
        if (downDev == 0) return 0;
        // 일봉: sqrt(252), 분봉: sqrt(252 * 6.5 * 12) 연율화 인수
        var annFactor = tf == TimeFrame.Daily ? (decimal)Math.Sqrt(252) : (decimal)Math.Sqrt(252 * 6.5 * 12);
        return mean / downDev * annFactor;
    }

    /// <summary>
    /// Calmar 비율: 연율화 수익률 / 최대 낙폭. 3이상이면 우수.
    /// </summary>
    public static decimal ComputeCalmarRatio(decimal annualizedReturn, decimal maxDrawdown)
        => maxDrawdown > 0 ? annualizedReturn / maxDrawdown : 0;

    /// <summary>
    /// Profit Factor: 총 수익 / 총 손실. 1.5이상 양호, 2이상 우수.
    /// </summary>
    public static decimal ComputeProfitFactor(List<TradeRecord> trades)
    {
        var wins   = trades.Where(t => t.PnL > 0).Sum(t => t.PnL);
        var losses = Math.Abs(trades.Where(t => t.PnL < 0).Sum(t => t.PnL));
        return losses > 0 ? wins / losses : wins > 0 ? 99.9m : 0;
    }

    /// <summary>
    /// 연율화 수익률 (CAGR): (1 + totalReturn)^(1/years) - 1
    /// </summary>
    public static decimal ComputeAnnualizedReturn(decimal totalReturn, int tradingDays)
    {
        if (tradingDays <= 0) return 0;
        var years = tradingDays / 252.0;
        if (years <= 0) return 0;
        var factor = 1 + totalReturn / 100m;
        if (factor <= 0) return -100;
        return ((decimal)Math.Pow((double)factor, 1.0 / years) - 1) * 100;
    }

    // ── [E-1] Kelly Criterion ─────────────────────────────────────────────

    /// <summary>
    /// Kelly Criterion: f* = p - q/b
    /// 최대 25%로 상한 제한 (Half-Kelly 권장).
    /// </summary>
    public static decimal ComputeKellyFraction(decimal winRate, decimal avgWinPct, decimal avgLossPct)
    {
        if (avgLossPct <= 0) return 0;
        var b = avgWinPct / avgLossPct; // 손익비
        var p = winRate;
        var q = 1 - p;
        var kelly = p - q / b;
        return Math.Max(0, Math.Min(0.25m, kelly));
    }

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
                if (stdDev > 0) sharpe = avg / stdDev * (decimal)Math.Sqrt(252.0);
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
        // 표본 분산(sample variance): n-1 분모 사용. n==1이면 stdDev=0 → 0 반환
        var variance = n > 1 ? sumSqDiff / (n - 1) : 0m;
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

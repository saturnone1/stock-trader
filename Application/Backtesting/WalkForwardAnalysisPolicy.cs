using StockTrader.Models;

namespace StockTrader.Application.Backtesting;

public sealed record WalkForwardPeriod(
    DateTime InSampleFrom,
    DateTime InSampleTo,
    DateTime OutOfSampleFrom,
    DateTime OutOfSampleTo);

public sealed record WalkForwardPlan(
    IReadOnlyList<WalkForwardPeriod> Periods,
    string? ValidationError = null)
{
    public bool IsValid => ValidationError is null;
}

/// <summary>워크포워드 기간과 집계 수학을 실행 인프라에서 분리해 결정론적으로 유지한다.</summary>
public static class WalkForwardAnalysisPolicy
{
    public static WalkForwardPlan BuildPlan(
        DateTime from,
        DateTime to,
        int inSampleMonths,
        int outOfSampleMonths)
    {
        if (inSampleMonths <= 0)
            return new([], "워크포워드 학습 기간은 1개월 이상이어야 합니다.");
        if (outOfSampleMonths <= 0)
            return new([], "워크포워드 검증 기간은 1개월 이상이어야 합니다.");
        if (from.Date >= to.Date)
            return new([], "워크포워드 종료일은 시작일보다 뒤여야 합니다.");

        var periods = new List<WalkForwardPeriod>();
        var windowStart = from.Date;
        try
        {
            while (true)
            {
                var outOfSampleStart = windowStart.AddMonths(inSampleMonths);
                var nextWindowStart = outOfSampleStart.AddMonths(outOfSampleMonths);
                if (nextWindowStart > to.Date)
                    break;

                periods.Add(new WalkForwardPeriod(
                    windowStart,
                    outOfSampleStart.AddDays(-1),
                    outOfSampleStart,
                    nextWindowStart.AddDays(-1)));
                windowStart = nextWindowStart;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return new([], "워크포워드 기간 설정이 지원되는 날짜 범위를 벗어났습니다.");
        }

        return periods.Count > 0
            ? new(periods)
            : new([], "선택 기간 안에 완전한 워크포워드 학습·검증 창이 없습니다.");
    }

    public static WalkForwardResult Aggregate(IReadOnlyList<WalkForwardWindow> windows)
    {
        var totalInSampleReturn = windows.Sum(window => window.InSampleReturnPercent);
        var totalOutOfSampleReturn = windows.Sum(window => window.OutOfSampleReturnPercent);
        return new WalkForwardResult
        {
            Windows = windows.ToList(),
            AggregateOosReturn = windows.Sum(window => window.OutOfSampleReturn),
            AggregateOosReturnPercent = windows.Count > 0
                ? windows.Average(window => window.OutOfSampleReturnPercent)
                : 0,
            AggregateOosMaxDrawdown = windows.Count > 0
                ? windows.Max(window => window.OutOfSampleMaxDrawdown)
                : 0,
            AggregateOosWinRate = windows.Count > 0
                ? (decimal)windows.Count(window => window.OutOfSampleReturnPercent > 0) / windows.Count
                : 0,
            AggregateOosSharpe = windows.Count > 0
                ? windows.Average(window => window.OutOfSampleSharpe)
                : 0,
            WalkForwardEfficiency = totalInSampleReturn > 0
                ? totalOutOfSampleReturn / totalInSampleReturn
                : 0
        };
    }
}

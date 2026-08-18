namespace StockTrader.Application.Backtesting;

/// <summary>
/// 백테스트 전체 평가기간의 복리 수익률과 낙폭 기반 지표를 계산합니다.
/// 모든 입력 수익률과 낙폭은 0.10 = 10%인 비율 단위입니다.
/// </summary>
public static class BacktestPerformancePolicy
{
    private const double CalendarDaysPerYear = 365.25;
    // Sub-day CAGR is both misleading and numerically explosive; retain the historical one-day floor.
    private const double MinimumAnnualizationDays = 1.0;
    private const decimal CompleteLossFraction = -1m;
    // Values beyond one million percent are not decision-useful and cannot safely cross decimal DTOs.
    private const decimal MaximumAnnualizedReturnFraction = 10_000m;
    private const decimal NoDownsideSortinoCap = 10m;

    public static BacktestPeriodPerformance Evaluate(
        decimal totalReturnFraction,
        decimal maxDrawdownFraction,
        IReadOnlyList<decimal> completedTradeReturnFractions,
        DateTime evaluationFrom,
        DateTime evaluationTo)
    {
        var annualizedReturnFraction = ComputeAnnualizedReturnFraction(
            totalReturnFraction,
            evaluationFrom,
            evaluationTo);
        var calmarRatio = maxDrawdownFraction > 0
            ? annualizedReturnFraction / maxDrawdownFraction
            : 0m;
        var sharpeRatio = ComputeSharpeRatio(
            completedTradeReturnFractions,
            evaluationFrom,
            evaluationTo);
        var sortinoRatio = ComputeSortinoRatio(
            completedTradeReturnFractions,
            evaluationFrom,
            evaluationTo);

        return new BacktestPeriodPerformance(
            annualizedReturnFraction,
            calmarRatio,
            sharpeRatio,
            sortinoRatio);
    }

    public static decimal ComputeAnnualizedReturnFraction(
        decimal totalReturnFraction,
        DateTime evaluationFrom,
        DateTime evaluationTo)
    {
        var elapsedCalendarDays = (evaluationTo - evaluationFrom).TotalDays;
        if (elapsedCalendarDays <= 0) return 0m;

        var growthFactor = 1m + totalReturnFraction;
        if (growthFactor <= 0) return CompleteLossFraction;

        var calendarDays = Math.Max(MinimumAnnualizationDays, elapsedCalendarDays);
        var years = calendarDays / CalendarDaysPerYear;
        var annualizedGrowthFactor = Math.Pow((double)growthFactor, 1.0 / years);
        if (!double.IsFinite(annualizedGrowthFactor)
            || annualizedGrowthFactor - 1.0 >= (double)MaximumAnnualizedReturnFraction)
        {
            return MaximumAnnualizedReturnFraction;
        }

        return (decimal)annualizedGrowthFactor - 1m;
    }

    public static decimal ComputeSharpeRatio(
        IReadOnlyList<decimal> completedTradeReturnFractions,
        DateTime evaluationFrom,
        DateTime evaluationTo)
    {
        if (completedTradeReturnFractions.Count < 2) return 0m;

        var mean = completedTradeReturnFractions.Average();
        var sumSquaredDifference = completedTradeReturnFractions.Sum(value =>
        {
            var difference = value - mean;
            return difference * difference;
        });
        var standardDeviation = (decimal)Math.Sqrt(
            (double)(sumSquaredDifference / (completedTradeReturnFractions.Count - 1)));

        return standardDeviation > 0
            ? mean / standardDeviation * AnnualizationFactor(
                completedTradeReturnFractions.Count,
                evaluationFrom,
                evaluationTo)
            : 0m;
    }

    public static decimal ComputeSortinoRatio(
        IReadOnlyList<decimal> completedTradeReturnFractions,
        DateTime evaluationFrom,
        DateTime evaluationTo)
    {
        if (completedTradeReturnFractions.Count < 2) return 0m;

        var mean = completedTradeReturnFractions.Average();
        var downsideSquares = completedTradeReturnFractions
            .Select(value => value < 0 ? value * value : 0m)
            .ToList();
        if (downsideSquares.All(value => value == 0m))
            return mean > 0 ? NoDownsideSortinoCap : 0m;

        var downsideDeviation = (decimal)Math.Sqrt((double)downsideSquares.Average());
        return downsideDeviation > 0
            ? mean / downsideDeviation * AnnualizationFactor(
                completedTradeReturnFractions.Count,
                evaluationFrom,
                evaluationTo)
            : 0m;
    }

    private static decimal AnnualizationFactor(
        int completedTradeCount,
        DateTime evaluationFrom,
        DateTime evaluationTo)
    {
        var elapsedCalendarDays = (evaluationTo - evaluationFrom).TotalDays;
        if (completedTradeCount <= 0 || elapsedCalendarDays <= 0) return 0m;

        var calendarDays = Math.Max(MinimumAnnualizationDays, elapsedCalendarDays);
        var tradesPerYear = completedTradeCount / calendarDays * CalendarDaysPerYear;
        return (decimal)Math.Sqrt(tradesPerYear);
    }
}

public sealed record BacktestPeriodPerformance(
    decimal AnnualizedReturnFraction,
    decimal CalmarRatio,
    decimal SharpeRatio,
    decimal SortinoRatio);

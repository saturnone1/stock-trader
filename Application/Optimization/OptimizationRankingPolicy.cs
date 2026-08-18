using StockTrader.Domain.Optimization;

namespace StockTrader.Application.Optimization;

public readonly record struct OptimizationMetricValues(
    decimal TotalReturn,
    decimal SortinoRatio,
    decimal SharpeRatio,
    decimal CalmarRatio,
    decimal ProfitFactor,
    decimal WinRate,
    decimal AnnualizedReturn);

/// <summary>모든 최적화 실행 방식에 동일한 순위 의미와 정렬 방향을 적용한다.</summary>
public static class OptimizationRankingPolicy
{
    public static IOrderedEnumerable<T> OrderDescending<T>(
        IEnumerable<T> results,
        string? rankBy,
        Func<T, OptimizationMetricValues> metrics)
    {
        var metric = OptimizationRankingCatalog.MetricFor(rankBy);
        return results.OrderByDescending(result => Select(metrics(result), metric));
    }

    private static decimal Select(OptimizationMetricValues values, OptimizationRankMetric metric) =>
        metric switch
        {
            OptimizationRankMetric.TotalReturn => values.TotalReturn,
            OptimizationRankMetric.SharpeRatio => values.SharpeRatio,
            OptimizationRankMetric.CalmarRatio => values.CalmarRatio,
            OptimizationRankMetric.ProfitFactor => values.ProfitFactor,
            OptimizationRankMetric.WinRate => values.WinRate,
            OptimizationRankMetric.AnnualizedReturn => values.AnnualizedReturn,
            _ => values.SortinoRatio
        };
}

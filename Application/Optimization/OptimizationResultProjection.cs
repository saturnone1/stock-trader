using StockTrader.Models;

namespace StockTrader.Application.Optimization;

public sealed record OptimizationPerformanceMetrics(
    decimal TotalReturn,
    decimal SortinoRatio,
    decimal SharpeRatio,
    decimal MaxDrawdown,
    decimal WinRate,
    int TotalTrades,
    decimal ProfitFactor,
    decimal CalmarRatio,
    decimal AnnualizedReturn);

/// <summary>백테스트 결과를 최적화 순위 및 OOS 지표 단위로 변환하는 단일 투영기입니다.</summary>
public static class OptimizationResultProjection
{
    public static OptimizationPerformanceMetrics FromBacktest(BacktestResult result) => new(
        result.TotalReturnPercent * 100,
        result.SortinoRatio,
        result.SharpeRatio,
        result.MaxDrawdown * 100,
        result.OverallWinRate * 100,
        result.TotalTrades,
        result.ProfitFactor,
        result.CalmarRatio,
        result.AnnualizedReturn);

    public static OptimizeResultItem ToResultItem(
        OptimizeParamSnapshot parameters,
        BacktestResult result)
    {
        var metrics = FromBacktest(result);
        return new OptimizeResultItem
        {
            Params = parameters,
            TotalReturn = metrics.TotalReturn,
            SortinoRatio = metrics.SortinoRatio,
            SharpeRatio = metrics.SharpeRatio,
            MaxDrawdown = metrics.MaxDrawdown,
            WinRate = metrics.WinRate,
            TotalTrades = metrics.TotalTrades,
            ProfitFactor = metrics.ProfitFactor,
            CalmarRatio = metrics.CalmarRatio,
            AnnualizedReturn = metrics.AnnualizedReturn
        };
    }

    public static void ApplyOutOfSample(OptimizeResultItem item, BacktestResult result)
    {
        var metrics = FromBacktest(result);
        item.OosTotalReturn = metrics.TotalReturn;
        item.OosSortinoRatio = metrics.SortinoRatio;
        item.OosSharpeRatio = metrics.SharpeRatio;
        item.OosMaxDrawdown = metrics.MaxDrawdown;
        item.OosWinRate = metrics.WinRate;
        item.OosTotalTrades = metrics.TotalTrades;
        item.OosProfitFactor = metrics.ProfitFactor;
        item.OosCalmarRatio = metrics.CalmarRatio;
        item.OosAnnualizedReturn = metrics.AnnualizedReturn;
    }
}

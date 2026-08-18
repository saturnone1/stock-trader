using StockTrader.Models;

namespace StockTrader.Api.Contracts;

public sealed record BacktestErrorResponse(string Error);

public sealed record BacktestStrategyPerformanceResponse(
    int SampleSize,
    decimal WinRate,
    decimal AvgWinPercent,
    decimal AvgLossPercent,
    decimal Expectancy,
    decimal ProfitFactor)
{
    public static BacktestStrategyPerformanceResponse Create(PatternStats value) => new(
        value.SampleSize,
        value.WinRate,
        value.AvgWinPercent,
        value.AvgLossPercent,
        value.Expectancy,
        value.ProfitFactor);
}

public sealed record BacktestSymbolPerformanceResponse(
    string Symbol,
    int TradeCount,
    decimal WinRate,
    decimal TotalPnL,
    decimal AvgPnLPercent);

public sealed record BacktestRegimePerformanceResponse(
    int TradeCount,
    decimal WinRate,
    decimal TotalPnL,
    decimal AverageTradeReturn,
    decimal ProfitFactor);

public sealed record BacktestEquityPointResponse(string Timestamp, decimal Equity);

public sealed record BacktestTradeResponse(
    string Symbol,
    string Pattern,
    string? CustomPatternName,
    string EntryTime,
    string ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    decimal NetPnL,
    decimal ReturnPct,
    string ExitReason);

public sealed record BacktestWalkForwardWindowResponse(
    string IsFrom,
    string IsTo,
    string OosFrom,
    string OosTo,
    int InSampleTrades,
    decimal InSampleReturnPercent,
    int OutOfSampleTrades,
    decimal OutOfSampleReturnPercent,
    decimal OutOfSampleMaxDrawdown,
    decimal OutOfSampleSharpe,
    decimal Efficiency);

public sealed record BacktestWalkForwardResponse(
    decimal AggregateOosReturnPercent,
    decimal AggregateOosMaxDrawdown,
    decimal AggregateOosWinRate,
    decimal AggregateOosSharpe,
    decimal WalkForwardEfficiency,
    IReadOnlyList<BacktestWalkForwardWindowResponse> Windows);

public sealed record BacktestMonteCarloResponse(
    int Simulations,
    decimal MedianFinalEquity,
    decimal MeanFinalEquity,
    decimal Percentile5Equity,
    decimal Percentile25Equity,
    decimal Percentile75Equity,
    decimal Percentile95Equity,
    decimal MedianMaxDrawdown,
    decimal WorstCaseMaxDrawdown,
    decimal ProbabilityOfLoss);

public sealed record BacktestResponse(
    int TotalTrades,
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal SortinoRatio,
    decimal CalmarRatio,
    decimal ProfitFactor,
    decimal AnnualizedReturn,
    decimal OverallWinRate,
    decimal KellyFraction,
    decimal HalfKellyFraction,
    decimal AvgMaePercent,
    decimal AvgMfePercent,
    decimal MedianMaePercent,
    decimal MedianMfePercent,
    decimal TotalSlippageCost,
    decimal TotalCommissionCost,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings,
    string? SurvivorshipBiasWarning,
    bool WeightStrategyApplied,
    int WeightReducedTrades,
    string UsedTimeFrame,
    string? ActualDataFrom,
    IReadOnlyDictionary<string, BacktestStrategyPerformanceResponse> PerPattern,
    IReadOnlyDictionary<string, BacktestStrategyPerformanceResponse> PerStrategy,
    IReadOnlyList<BacktestSymbolPerformanceResponse> PerSymbol,
    IReadOnlyDictionary<string, BacktestRegimePerformanceResponse> PerRegimeStats,
    IReadOnlyList<BacktestEquityPointResponse> EquityCurve,
    IReadOnlyList<BacktestTradeResponse> Trades,
    BacktestWalkForwardResponse? WalkForward,
    BacktestMonteCarloResponse? MonteCarlo)
{
    private const int MaximumEquityPoints = 300;

    public static BacktestResponse Create(BacktestResult result)
    {
        var equityCurve = Downsample(result.EquityCurve);
        return new BacktestResponse(
            result.TotalTrades,
            result.TotalReturnPercent,
            result.MaxDrawdown,
            result.SharpeRatio,
            result.SortinoRatio,
            result.CalmarRatio,
            result.ProfitFactor,
            result.AnnualizedReturn,
            result.OverallWinRate,
            result.KellyFraction,
            result.HalfKellyFraction,
            result.AvgMaePercent,
            result.AvgMfePercent,
            result.MedianMaePercent,
            result.MedianMfePercent,
            result.TotalSlippageCost,
            result.TotalCommissionCost,
            result.ErrorMessage,
            result.Warnings,
            result.SurvivorshipBiasWarning,
            result.WeightStrategyApplied,
            result.WeightReducedTrades,
            result.UsedTimeFrame.ToString(),
            result.ActualDataFrom?.ToString("O"),
            result.PerPatternStats.ToDictionary(
                item => item.Key.ToString(),
                item => BacktestStrategyPerformanceResponse.Create(item.Value)),
            result.PerStrategyStats.ToDictionary(
                item => item.Key,
                item => BacktestStrategyPerformanceResponse.Create(item.Value),
                StringComparer.OrdinalIgnoreCase),
            result.PerSymbolStats.Select(item => new BacktestSymbolPerformanceResponse(
                item.Symbol,
                item.TradeCount,
                item.WinRate,
                item.TotalPnL,
                item.AvgPnLPercent)).ToArray(),
            (result.PerRegimeStats ?? []).ToDictionary(
                item => item.Key,
                item => new BacktestRegimePerformanceResponse(
                    item.Value.TradeCount,
                    item.Value.WinRate,
                    item.Value.TotalPnL,
                    item.Value.AverageTradeReturn,
                    item.Value.ProfitFactor)),
            equityCurve.Select(item => new BacktestEquityPointResponse(
                item.Date.ToString("O"),
                item.Equity)).ToArray(),
            result.Trades.Select(item => new BacktestTradeResponse(
                item.Symbol,
                item.PatternType.ToString(),
                item.CustomPatternName,
                item.EntryTime.ToString("O"),
                item.ExitTime.ToString("O"),
                item.EntryPrice,
                item.ExitPrice,
                item.Quantity,
                item.PnL,
                item.PnLPercent,
                item.ExitReason)).ToArray(),
            CreateWalkForward(result.WalkForward),
            CreateMonteCarlo(result.MonteCarlo));
    }

    private static BacktestWalkForwardResponse? CreateWalkForward(WalkForwardResult? value) =>
        value is null ? null : new BacktestWalkForwardResponse(
            value.AggregateOosReturnPercent,
            value.AggregateOosMaxDrawdown,
            value.AggregateOosWinRate,
            value.AggregateOosSharpe,
            value.WalkForwardEfficiency,
            value.Windows.Select(item => new BacktestWalkForwardWindowResponse(
                item.InSampleFrom.ToString("O"),
                item.InSampleTo.ToString("O"),
                item.OutOfSampleFrom.ToString("O"),
                item.OutOfSampleTo.ToString("O"),
                item.InSampleTrades,
                item.InSampleReturnPercent,
                item.OutOfSampleTrades,
                item.OutOfSampleReturnPercent,
                item.OutOfSampleMaxDrawdown,
                item.OutOfSampleSharpe,
                item.Efficiency)).ToArray());

    private static BacktestMonteCarloResponse? CreateMonteCarlo(MonteCarloResult? value) =>
        value is null ? null : new BacktestMonteCarloResponse(
            value.Simulations,
            value.MedianFinalEquity,
            value.MeanFinalEquity,
            value.Percentile5Equity,
            value.Percentile25Equity,
            value.Percentile75Equity,
            value.Percentile95Equity,
            value.MedianMaxDrawdown,
            value.WorstCaseMaxDrawdown,
            value.ProbabilityOfLoss);

    private static IReadOnlyList<EquityPoint> Downsample(IReadOnlyList<EquityPoint> points)
    {
        if (points.Count <= MaximumEquityPoints) return points;

        var sampled = new List<EquityPoint>(MaximumEquityPoints) { points[0] };
        var step = (double)(points.Count - 1) / (MaximumEquityPoints - 1);
        for (var index = 1; index < MaximumEquityPoints - 1; index++)
            sampled.Add(points[(int)Math.Round(index * step)]);
        sampled.Add(points[^1]);
        return sampled;
    }
}

using StockTrader.Application.Backtesting;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>완료된 체결과 자본 곡선으로 사용자에게 반환할 성과 지표를 구성합니다.</summary>
internal static class BacktestResultBuilder
{
    private static readonly HashSet<string> SurvivorshipSensitiveSymbols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TQQQ", "SOXL", "UPRO", "TECL", "FNGU", "SPXL", "QLD", "UDOW"
        };

    public static BacktestResult Build(BacktestResultInputs input)
    {
        var trades = input.Trades.OrderBy(trade => trade.EntryTime).ToList();
        var tradeCycles = PerformanceCalculator.AggregateTradeCycles(trades);
        var totalReturn = input.CurrentEquity - input.InitialCapital;
        var totalReturnPercent = input.InitialCapital > 0
            ? totalReturn / input.InitialCapital
            : 0;
        var winCount = tradeCycles.Count(trade => trade.IsWin);
        var winRate = tradeCycles.Count > 0 ? (decimal)winCount / tradeCycles.Count : 0;

        var periodPerformance = BacktestPerformancePolicy.Evaluate(
            totalReturnPercent,
            input.MaxDrawdown,
            tradeCycles.Select(trade => trade.PnLPercent).ToList(),
            input.From,
            input.To);
        var annualizedReturnPercent = periodPerformance.AnnualizedReturnFraction * 100m;
        var profitFactor = PerformanceCalculator.ComputeProfitFactor(tradeCycles);

        var perPatternStats = PerformanceCalculator.ComputePerPatternStats(
            tradeCycles,
            input.To);
        var perStrategyStats = PerformanceCalculator.ComputePerStrategyStats(
            tradeCycles,
            input.To);
        var (kellyFraction, halfKellyFraction) = ComputeKelly(tradeCycles, winRate);
        var (avgMae, avgMfe, medianMae, medianMfe) =
            PerformanceCalculator.ComputeMaeMfe(tradeCycles);

        var spyAbove200Ma = input.RegimeByDate.ToDictionary(
            pair => pair.Key.ToDateTime(TimeOnly.MinValue),
            pair => pair.Value.SpyAbove200Ma);
        var perRegimeStats = PerformanceCalculator.ComputeRegimeStats(tradeCycles, spyAbove200Ma);
        var survivorshipWarning = BuildSurvivorshipWarning(input.Symbols, input.From, input.To);

        if (kellyFraction > 0 && tradeCycles.Count >= 10)
        {
            var recommendedSize = Math.Round(halfKellyFraction * 100, 1);
            var winRatePercent = Math.Round(winRate * 100, 1);
            input.Warnings.Add($"[권장 파라미터] Half-Kelly 포지션 크기: {recommendedSize}% | " +
                               $"WinRate {winRatePercent}% | ProfitFactor {profitFactor:F2} | Sortino {periodPerformance.SortinoRatio:F2}");
        }

        return new BacktestResult
        {
            Trades = trades,
            TotalReturn = totalReturn,
            TotalReturnPercent = totalReturnPercent,
            MaxDrawdown = input.MaxDrawdown,
            SharpeRatio = periodPerformance.SharpeRatio,
            TotalTrades = tradeCycles.Count,
            OverallWinRate = winRate,
            PerPatternStats = perPatternStats,
            PerStrategyStats = perStrategyStats,
            PerSymbolStats = PerformanceCalculator.ComputePerSymbolStats(tradeCycles, input.InitialCapital),
            EquityCurve = input.EquityCurve,
            TotalSlippageCost = input.TotalSlippage,
            TotalCommissionCost = input.TotalCommission,
            WeightStrategyApplied = input.WeightStrategyApplied,
            WeightReducedTrades = input.WeightReducedTrades,
            Warnings = input.Warnings,
            ActualDataFrom = input.ActualDataFrom,
            SortinoRatio = periodPerformance.SortinoRatio,
            CalmarRatio = periodPerformance.CalmarRatio,
            ProfitFactor = profitFactor,
            AnnualizedReturn = annualizedReturnPercent,
            KellyFraction = kellyFraction,
            HalfKellyFraction = halfKellyFraction,
            SurvivorshipBiasWarning = survivorshipWarning,
            PerRegimeStats = perRegimeStats,
            AvgMaePercent = avgMae,
            AvgMfePercent = avgMfe,
            MedianMaePercent = medianMae,
            MedianMfePercent = medianMfe
        };
    }

    private static (decimal Kelly, decimal HalfKelly) ComputeKelly(
        IReadOnlyCollection<TradeRecord> trades,
        decimal winRate)
    {
        if (trades.Count == 0) return (0, 0);

        var wins = trades.Where(trade => trade.PnL > 0).ToList();
        var losses = trades.Where(trade => trade.PnL < 0).ToList();
        var averageWinPercent = wins.Count > 0
            ? wins.Average(trade => trade.PnLPercent * 100)
            : 0;
        var averageLossPercent = losses.Count > 0
            ? Math.Abs(losses.Average(trade => trade.PnLPercent * 100))
            : 0;
        var kelly = PerformanceCalculator.ComputeKellyFraction(
            winRate, averageWinPercent, averageLossPercent);
        return (kelly, kelly / 2);
    }

    private static string? BuildSurvivorshipWarning(
        IReadOnlyCollection<string> symbols,
        DateTime from,
        DateTime to)
    {
        var dateRangeYears = (to - from).TotalDays / 365.0;
        if (symbols.Count > 5 || dateRangeYears < 3
            || !symbols.Any(SurvivorshipSensitiveSymbols.Contains))
        {
            return null;
        }

        return "생존자 편향 주의: 고레버리지/고성과 ETF(TQQQ 등)만으로 장기 백테스트 시 " +
               "결과가 과대 추정될 수 있습니다. 다양한 종목으로 검증하세요.";
    }
}

internal sealed record BacktestResultInputs
{
    public required IReadOnlyCollection<string> Symbols { get; init; }
    public required IReadOnlyCollection<TradeRecord> Trades { get; init; }
    public required Dictionary<DateOnly, MarketRegime> RegimeByDate { get; init; }
    public required List<EquityPoint> EquityCurve { get; init; }
    public required List<string> Warnings { get; init; }
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required decimal InitialCapital { get; init; }
    public required decimal CurrentEquity { get; init; }
    public required decimal MaxDrawdown { get; init; }
    public required decimal TotalSlippage { get; init; }
    public required decimal TotalCommission { get; init; }
    public required bool WeightStrategyApplied { get; init; }
    public required int WeightReducedTrades { get; init; }
    public DateTime? ActualDataFrom { get; init; }
}

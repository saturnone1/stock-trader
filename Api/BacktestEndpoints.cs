using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.LiveParameter;

namespace StockTrader.Api;

public sealed record ApplyLiveRequest(
    PatternParameterOverrides? ParameterOverrides,
    List<string>? EnabledPatterns,
    decimal? RiskPerTradePercent,
    decimal? DailyLossLimitPercent,
    int? MaxTotalPositions,
    int? MaxPositionsPerSector);

public static class BacktestEndpoints
{
    public static RouteGroupBuilder MapBacktestApi(this RouteGroupBuilder api)
    {
        api.MapPost("/backtest", RunAsync).RequireAuthorization();
        api.MapPost("/backtest/apply-live", ApplyLiveAsync).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> RunAsync(
        BacktestRequest request,
        IBacktestService service,
        CancellationToken ct)
    {
        if (string.Equals(request.BacktestMode, "weight", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "weight 백테스트 모드는 제거되었습니다. 패턴 백테스트 또는 패턴 빌더를 사용하세요." });

        var result = await service.RunAsync(request, ct);
        var equityCurve = Downsample(result.EquityCurve);
        return Results.Ok(new
        {
            result.TotalTrades,
            TotalReturn = result.TotalReturnPercent,
            result.MaxDrawdown,
            result.SharpeRatio,
            result.OverallWinRate,
            result.TotalSlippageCost,
            result.TotalCommissionCost,
            result.ErrorMessage,
            result.Warnings,
            result.WeightStrategyApplied,
            result.WeightReducedTrades,
            UsedTimeFrame = result.UsedTimeFrame.ToString(),
            ActualDataFrom = result.ActualDataFrom?.ToString("yyyy-MM-dd"),
            PerPattern = result.PerPatternStats.ToDictionary(
                item => item.Key.ToString(),
                item => new { item.Value.SampleSize, item.Value.WinRate, item.Value.AvgWinPercent, item.Value.AvgLossPercent, item.Value.Expectancy, item.Value.ProfitFactor }),
            PerStrategy = result.PerStrategyStats.ToDictionary(
                item => item.Key,
                item => new { item.Value.SampleSize, item.Value.WinRate, item.Value.AvgWinPercent, item.Value.AvgLossPercent, item.Value.Expectancy, item.Value.ProfitFactor }),
            PerSymbol = result.PerSymbolStats.Select(item => new
            {
                item.Symbol, item.TradeCount, item.WinRate, item.TotalPnL, item.AvgPnLPercent,
            }),
            EquityCurve = equityCurve.Select(item => new { Date = item.Date.ToString("yyyy-MM-dd"), item.Equity }),
            Trades = result.Trades.Select(item => new
            {
                item.Symbol,
                Pattern = item.PatternType.ToString(),
                item.CustomPatternName,
                EntryTime = item.EntryTime.ToString("yyyy-MM-dd"),
                ExitTime = item.ExitTime.ToString("yyyy-MM-dd"),
                item.EntryPrice,
                item.ExitPrice,
                ReturnPct = item.EntryPrice > 0 ? (item.ExitPrice - item.EntryPrice) / item.EntryPrice : 0m,
                item.ExitReason,
            }),
            WalkForward = result.WalkForward == null ? null : new
            {
                result.WalkForward.AggregateOosReturnPercent,
                result.WalkForward.AggregateOosMaxDrawdown,
                result.WalkForward.AggregateOosWinRate,
                result.WalkForward.AggregateOosSharpe,
                result.WalkForward.WalkForwardEfficiency,
                Windows = result.WalkForward.Windows.Select(item => new
                {
                    IsFrom = item.InSampleFrom.ToString("yyyy-MM-dd"),
                    IsTo = item.InSampleTo.ToString("yyyy-MM-dd"),
                    OosFrom = item.OutOfSampleFrom.ToString("yyyy-MM-dd"),
                    OosTo = item.OutOfSampleTo.ToString("yyyy-MM-dd"),
                    item.InSampleTrades,
                    item.InSampleReturnPercent,
                    item.OutOfSampleTrades,
                    item.OutOfSampleReturnPercent,
                    item.OutOfSampleMaxDrawdown,
                    item.OutOfSampleSharpe,
                    item.Efficiency,
                }),
            },
            MonteCarlo = result.MonteCarlo == null ? null : new
            {
                result.MonteCarlo.Simulations,
                result.MonteCarlo.MedianFinalEquity,
                result.MonteCarlo.MeanFinalEquity,
                result.MonteCarlo.Percentile5Equity,
                result.MonteCarlo.Percentile25Equity,
                result.MonteCarlo.Percentile75Equity,
                result.MonteCarlo.Percentile95Equity,
                result.MonteCarlo.MedianMaxDrawdown,
                result.MonteCarlo.WorstCaseMaxDrawdown,
                result.MonteCarlo.ProbabilityOfLoss,
            },
        });
    }

    private static async Task<IResult> ApplyLiveAsync(
        ApplyLiveRequest request,
        ILiveParameterService liveParameters,
        CancellationToken ct)
    {
        await liveParameters.ApplyToLiveAsync(
            request.ParameterOverrides ?? new PatternParameterOverrides(),
            request.EnabledPatterns?.Select(Enum.Parse<PatternType>).ToList() ?? [],
            request.RiskPerTradePercent ?? 0.01m,
            request.DailyLossLimitPercent ?? 0.03m,
            request.MaxTotalPositions ?? 7,
            request.MaxPositionsPerSector ?? 2,
            ct);
        return Results.Ok(new { message = "실거래 파라미터가 적용되었습니다." });
    }

    private static IReadOnlyList<EquityPoint> Downsample(IReadOnlyList<EquityPoint> points)
    {
        if (points.Count <= 300)
            return points;
        var sampled = new List<EquityPoint>(300) { points[0] };
        var step = (double)(points.Count - 1) / 299;
        for (var index = 1; index < 299; index++)
            sampled.Add(points[(int)Math.Round(index * step)]);
        sampled.Add(points[^1]);
        return sampled;
    }
}

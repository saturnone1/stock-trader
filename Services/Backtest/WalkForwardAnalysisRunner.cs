using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

public sealed record WalkForwardAnalysisOutcome(
    WalkForwardResult? Result,
    string? Warning = null);

/// <summary>준비 데이터를 한 번 적재한 뒤 모든 IS/OOS 창을 공통 시뮬레이션 러너로 실행한다.</summary>
public sealed class WalkForwardAnalysisRunner
{
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestPreparedSimulationRunner _simulation;
    private readonly ILogger<WalkForwardAnalysisRunner> _logger;

    public WalkForwardAnalysisRunner(
        BacktestDataPreparer dataPreparer,
        BacktestPreparedSimulationRunner simulation,
        ILogger<WalkForwardAnalysisRunner> logger)
    {
        _dataPreparer = dataPreparer;
        _simulation = simulation;
        _logger = logger;
    }

    internal async Task<WalkForwardAnalysisOutcome> RunAsync(
        BacktestRequest request,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimes,
        BacktestRiskParameters risk,
        PatternSettings settings,
        CancellationToken ct)
    {
        var plan = WalkForwardAnalysisPolicy.BuildPlan(
            request.From,
            request.To,
            request.WalkForwardInSampleMonths,
            request.WalkForwardOutOfSampleMonths);
        if (!plan.IsValid)
        {
            _logger.LogWarning("워크포워드 분석 생략: {Reason}", plan.ValidationError);
            return new(null, plan.ValidationError);
        }

        _logger.LogInformation(
            "Walk-Forward 분석 시작 (IS:{InSample}개월, OOS:{OutOfSample}개월, {Count}개 창)",
            request.WalkForwardInSampleMonths,
            request.WalkForwardOutOfSampleMonths,
            plan.Periods.Count);
        var symbols = request.Symbols
            .Concat(BacktestDetectorMetadata.CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = await _dataPreparer.PrepareAsync(
            dataFeed,
            symbols,
            request.TimeFrame,
            request.From,
            request.To,
            settings.CumulativeRsi2,
            settings.Tqqq200Sma,
            ct);

        var windows = new List<WalkForwardWindow>(plan.Periods.Count);
        foreach (var period in plan.Periods)
        {
            ct.ThrowIfCancellationRequested();
            var inSample = await RunPeriodAsync(period.InSampleFrom, period.InSampleTo);
            var outOfSample = await RunPeriodAsync(period.OutOfSampleFrom, period.OutOfSampleTo);
            windows.Add(new WalkForwardWindow
            {
                InSampleFrom = period.InSampleFrom,
                InSampleTo = period.InSampleTo,
                OutOfSampleFrom = period.OutOfSampleFrom,
                OutOfSampleTo = period.OutOfSampleTo,
                InSampleTrades = inSample.TotalTrades,
                InSampleReturn = inSample.TotalReturn,
                InSampleReturnPercent = inSample.TotalReturnPercent,
                OutOfSampleTrades = outOfSample.TotalTrades,
                OutOfSampleReturn = outOfSample.TotalReturn,
                OutOfSampleReturnPercent = outOfSample.TotalReturnPercent,
                OutOfSampleMaxDrawdown = outOfSample.MaxDrawdown,
                OutOfSampleSharpe = outOfSample.SharpeRatio,
                Efficiency = inSample.TotalReturnPercent > 0
                    ? outOfSample.TotalReturnPercent / inSample.TotalReturnPercent
                    : 0
            });
        }

        var result = WalkForwardAnalysisPolicy.Aggregate(windows);
        _logger.LogInformation(
            "Walk-Forward 완료: {Count}개 윈도우, OOS 평균 수익률 {Average:P2}, WF 효율 {Efficiency:P2}",
            windows.Count,
            result.AggregateOosReturnPercent,
            result.WalkForwardEfficiency);
        return new(result);

        Task<BacktestResult> RunPeriodAsync(DateTime from, DateTime to) =>
            _simulation.RunAsync(
                request.Symbols,
                prepared.Symbols,
                detectors,
                regimes,
                from,
                to,
                request.InitialCapital,
                request.SlippagePercent,
                request.CommissionPerTrade,
                request.TimeFrame,
                risk,
                request.ParameterOverrides,
                request.SlippageModel,
                request.WeightStrategy,
                settings,
                ct);
    }
}

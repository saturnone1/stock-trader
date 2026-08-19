using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>준비된 데이터 위에서 최적화 후보를 동일한 백테스트 엔진으로 평가합니다.</summary>
public sealed class OptimizationCandidateEvaluator : IOptimizationCandidateEvaluator
{
    private readonly ICustomStrategyDetectorFactory _detectors;
    private readonly BacktestPreparedSimulationRunner _runner;
    private readonly PatternSettings _patternSettings;
    private readonly ILogger<OptimizationCandidateEvaluator> _logger;

    public OptimizationCandidateEvaluator(
        ICustomStrategyDetectorFactory detectors,
        BacktestPreparedSimulationRunner runner,
        IOptions<PatternSettings> patternSettings,
        ILogger<OptimizationCandidateEvaluator> logger)
    {
        _detectors = detectors;
        _runner = runner;
        _patternSettings = patternSettings.Value;
        _logger = logger;
    }

    public async Task<List<OptimizeResultItem>> EvaluateBatchAsync(
        OptimizationEvaluationContext context,
        IReadOnlyList<OptimizeParamSnapshot> combinations,
        DateTime from,
        DateTime to,
        string failureMessage,
        CancellationToken ct)
    {
        var results = new List<OptimizeResultItem>(combinations.Count);
        foreach (var combination in combinations)
        {
            var result = await RunAsync(
                context, combination, from, to, failureMessage, ct);
            if (result is not null)
            {
                results.Add(OptimizationResultProjection.ToResultItem(
                    combination, result));
            }
        }

        return results;
    }

    public async Task<BacktestResult?> RunAsync(
        OptimizationEvaluationContext context,
        OptimizeParamSnapshot combination,
        DateTime from,
        DateTime to,
        string failureMessage,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pattern = StrategyVariantFactory.CloneStrategyDocument(
            context.Request.BasePattern);
        StrategyVariantFactory.ApplyOptimizeOverrides(pattern, combination);
        var timeFrame = combination.TimeFrame.HasValue
            ? (TimeFrame)combination.TimeFrame.Value
            : context.Request.TimeFrame;
        var prepared = context.DataByTimeFrame.TryGetValue(timeFrame, out var selected)
            ? selected
            : context.DefaultData;

        try
        {
            return await _runner.RunAsync(
                context.Request.Symbols,
                prepared,
                context.EvidenceFor(timeFrame),
                [_detectors.Create(pattern)],
                context.Regimes,
                from,
                to,
                context.Request.InitialCapital,
                OptimizationBacktestAssumptions.SlippagePercent,
                OptimizationBacktestAssumptions.CommissionPerTrade,
                timeFrame,
                new BacktestRiskParameters(
                    context.Risk.RiskPerTradePercent,
                    context.Risk.DailyLossLimitPercent,
                    context.Risk.MaxTotalPositions,
                    context.Risk.MaxPositionsPerSector),
                null,
                OptimizationBacktestAssumptions.CostModel,
                null,
                _patternSettings,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{FailureMessage}", failureMessage);
            return null;
        }
    }
}

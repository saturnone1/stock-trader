using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

/// <summary>완료 일봉 기반의 실시간 패턴 스캔 유스케이스입니다.</summary>
public sealed class LivePatternScanCycle(
    ILiveDailyScanData data,
    ILiveMarketRegimeEvaluator regimeEvaluator,
    ILivePatternDetection detection,
    ILiveSignalProcessor signalProcessor,
    LivePatternScanState state,
    IMarketCalendar marketCalendar,
    TimeProvider timeProvider,
    ILogger<LivePatternScanCycle> logger) : ILivePatternScanCycle
{
    public async Task RunAsync(string symbol, CancellationToken ct = default)
    {
        var normalized = MarketSymbolPolicy.Normalize(symbol);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var context = await data.ResolveContextAsync(ct);
        var marketDate = DateOnly.FromDateTime(
            marketCalendar.GetLocalTime(context.MarketRegion, observedAt));
        if (state.WasScanned(normalized, context.Source, marketDate))
            return;

        var barSet = await data.LoadBarsAsync(
            normalized,
            observedAt.AddDays(-StrategyEvaluationPolicy.LiveDailySignalLookbackDays),
            observedAt,
            ct);
        if (barSet.Bars.Count < StrategyEvaluationPolicy.LiveScannerMinimumBars)
        {
            logger.LogDebug(
                "Skipping {Symbol}: only {Count} daily bars (need >= {Minimum})",
                normalized,
                barSet.Bars.Count,
                StrategyEvaluationPolicy.LiveScannerMinimumBars);
            return;
        }

        var regime = await state.GetRegimeAsync(
            context.RegimeBenchmarkSymbol,
            marketDate,
            () => LoadRegimeAsync(context.RegimeBenchmarkSymbol, observedAt, ct),
            ct);

        logger.LogDebug(
            "Scanning {Symbol}: {Count} daily bars, regime={Regime}",
            normalized,
            barSet.Bars.Count,
            regime.RegimeLabel);
        var detected = await detection.ScanSymbolAsync(
            normalized, barSet.Bars.ToArray(), regime, ct);
        if (detected.Count == 0)
        {
            logger.LogDebug("No signals for {Symbol}", normalized);
            state.MarkScanned(normalized, context.Source, marketDate);
            return;
        }

        logger.LogInformation(
            "Detected {Count} signal(s) for {Symbol}: {Patterns}",
            detected.Count,
            normalized,
            string.Join(", ", detected.Select(signal => signal.PatternType)));
        await signalProcessor.ProcessAsync(detected, barSet.Evidence, ct);
        state.MarkScanned(normalized, context.Source, marketDate);
    }

    private async Task<MarketRegime> LoadRegimeAsync(
        string benchmarkSymbol,
        DateTime observedAt,
        CancellationToken ct)
    {
        var bars = await data.LoadBarsAsync(
            benchmarkSymbol,
            observedAt.AddDays(-StrategyEvaluationPolicy.RegimeLookbackCalendarDays),
            observedAt,
            ct);
        logger.LogDebug(
            "ComputeRegime: {Symbol} daily bars count = {Count}",
            benchmarkSymbol,
            bars.Bars.Count);
        return regimeEvaluator.Evaluate(bars.Bars, observedAt);
    }
}

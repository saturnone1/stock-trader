using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using TimeZoneConverter;

namespace StockTrader.Services.Patterns;

/// <summary>완료 일봉 기반의 실시간 패턴 스캔 유스케이스입니다.</summary>
public sealed class LivePatternScanCycle(
    ILiveDailyScanData data,
    ILiveMarketRegimeEvaluator regimeEvaluator,
    ILivePatternDetection detection,
    ILiveSignalProcessor signalProcessor,
    LivePatternScanState state,
    TimeProvider timeProvider,
    ILogger<LivePatternScanCycle> logger) : ILivePatternScanCycle
{
    private static readonly TimeZoneInfo EasternTime =
        TZConvert.GetTimeZoneInfo("America/New_York");

    public async Task RunAsync(string symbol, CancellationToken ct = default)
    {
        var normalized = MarketSymbolPolicy.Normalize(symbol);
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var marketDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(observedAt, EasternTime));
        if (state.WasScanned(normalized, marketDate))
            return;

        var context = await data.ResolveContextAsync(ct);
        var bars = await data.LoadBarsAsync(
            normalized,
            observedAt.AddDays(-StrategyEvaluationPolicy.LiveDailySignalLookbackDays),
            observedAt,
            ct);
        if (bars.Count < StrategyEvaluationPolicy.LiveScannerMinimumBars)
        {
            logger.LogDebug(
                "Skipping {Symbol}: only {Count} daily bars (need >= {Minimum})",
                normalized,
                bars.Count,
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
            bars.Count,
            regime.RegimeLabel);
        var detected = await detection.ScanSymbolAsync(
            normalized, bars.ToArray(), regime, ct);
        if (detected.Count == 0)
        {
            logger.LogDebug("No signals for {Symbol}", normalized);
            state.MarkScanned(normalized, marketDate);
            return;
        }

        logger.LogInformation(
            "Detected {Count} signal(s) for {Symbol}: {Patterns}",
            detected.Count,
            normalized,
            string.Join(", ", detected.Select(signal => signal.PatternType)));
        await signalProcessor.ProcessAsync(detected, ct);
        state.MarkScanned(normalized, marketDate);
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
            bars.Count);
        return regimeEvaluator.Evaluate(bars, observedAt);
    }
}

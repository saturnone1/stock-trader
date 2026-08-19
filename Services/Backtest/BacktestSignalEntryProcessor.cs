using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 한 시점의 신규 진입 후보를 순서대로 평가하고 즉시 진입 또는 차기 시가 진입을 등록합니다.
/// 자격, 배분, 상관, 사이징 순서는 체결 결과의 일부이므로 이 처리기에서 함께 보존합니다.
/// </summary>
public sealed class BacktestSignalEntryProcessor
{
    private readonly ILogger<BacktestSignalEntryProcessor> _logger;

    public BacktestSignalEntryProcessor(ILogger<BacktestSignalEntryProcessor> logger)
    {
        _logger = logger;
    }

    internal async Task ProcessAsync(BacktestSignalEntryContext context, CancellationToken ct)
    {
        var openPositions = context.Portfolio.OpenPositions;
        foreach (var symbol in context.Symbols)
        {
            if (openPositions.ContainsKey(symbol)) continue;
            if (openPositions.Count >= context.MaxTotalPositions) break;
            if (!context.SymbolData.TryGetValue(symbol, out var data)) continue;
            if (!data.TimestampToIndex.TryGetValue(context.Date, out var barIndex)) continue;
            if (barIndex < BacktestDataPolicy.MinimumWarmupBars) continue;

            var windowSize = Math.Min(barIndex + 1, context.MaxWindow);
            var windowStart = barIndex + 1 - windowSize;
            var windowBars = data.Bars[windowStart..(barIndex + 1)];

            foreach (var detector in context.Detectors)
            {
                try
                {
                    var ruleDetector = detector as ICustomStrategyDetector;
                    var strategyRuntime = context.RuntimeRegistry.Find(
                        ruleDetector?.Definition.Name);
                    var portfolioRules = strategyRuntime?.Portfolio;
                    var cooldownUntil = context.RuntimeRegistry.GetCooldownUntil(
                        ruleDetector?.Definition.Name, symbol);
                    var entryEligibility = BacktestEntryEligibilityPolicy.Evaluate(
                        new BacktestEntryEligibilityRequest(
                            context.MaxTotalPositions,
                            portfolioRules?.MaxTotalPositions ?? 0,
                            openPositions.Count,
                            strategyRuntime?.CircuitBreakerTripped == true,
                            strategyRuntime?.CircuitBreaker.ConsecutiveLossLimit > 0,
                            context.TimelineIndex,
                            strategyRuntime?.CircuitBreakerUntilStep ?? 0,
                            portfolioRules?.MaxEntriesPerDay ?? 0,
                            strategyRuntime?.DailyEntryCount ?? 0,
                            barIndex,
                            cooldownUntil));
                    if (!entryEligibility.CanEnter) continue;

                    var signal = await detector.DetectAsync(symbol, windowBars, context.Regime, ct);
                    if (signal == null || signal.EntryPrice <= 0 || signal.StopLossPrice <= 0)
                        continue;

                    var stopDistance = Math.Abs(signal.EntryPrice - signal.StopLossPrice);
                    if (stopDistance <= 0) continue;

                    var baseEquity = Math.Max(
                        context.Portfolio.CurrentEquity,
                        context.InitialCapital * 0.10m);
                    var regimeScale = context.WeightStrategy != null
                        ? PositionAllocationPolicy.ResolveRegimeScale(
                            context.Regime, context.WeightStrategy)
                        : 1m;
                    var allocation = PositionAllocationPolicy.Apply(
                        baseEquity, regimeScale, signal.AllocationScale);
                    context.Portfolio.RegisterWeightReductions(allocation.ReductionCount);

                    if (portfolioRules?.MaxCorrelation > 0
                        && PortfolioCorrelationPolicy.ExceedsLimit(
                            symbol,
                            openPositions.Keys,
                            context.SymbolData,
                            context.Date,
                            portfolioRules.MaxCorrelation))
                        continue;

                    // 완료된 과거 거래만 사용해 켈리 비율 계산 (미래 거래 누출 방지)
                    var sizingTrades = ruleDetector == null
                        ? Array.Empty<PositionSizingTradeSample>()
                        : context.Trades
                            .Where(trade => string.Equals(
                                trade.CustomPatternName,
                                ruleDetector.Definition.Name,
                                StringComparison.OrdinalIgnoreCase))
                            .Select(trade => new PositionSizingTradeSample(
                                trade.PnL, trade.PnLPercent))
                            .ToArray();
                    var effectiveRisk = LongPositionSizingPolicy.ResolveRiskFraction(
                        context.RiskPerTrade,
                        ruleDetector?.Definition.SizingMode,
                        sizingTrades);
                    var sizing = LongPositionSizingPolicy.Calculate(new LongPositionSizingRequest(
                        allocation.EffectiveEquity,
                        effectiveRisk,
                        signal.EntryPrice,
                        signal.StopLossPrice,
                        entryEligibility.EffectiveMaxPositions,
                        portfolioRules?.MaxSinglePositionPercent ?? 0m));
                    if (!sizing.CanEnter) continue;

                    var entryAtr = data.Atr[barIndex] > 0
                        ? data.Atr[barIndex]
                        : stopDistance;
                    var customExit = ruleDetector == null
                        ? null
                        : LongPositionExitPolicyCatalog.ForCustom(ruleDetector.Definition);
                    var entryDefinition = ruleDetector?.Definition;
                    var isNextOpen = entryDefinition?.EntryMode == StrategyCatalog.NextOpenEntryMode;

                    if (isNextOpen && !context.PendingEntries.Contains(symbol))
                    {
                        signal.PendingEntry = true;
                        context.PendingEntries.TryAdd(symbol, new BacktestPendingEntry(
                            detector.PatternType,
                            entryDefinition!.Name,
                            signal.EntryPrice,
                            signal.StopLossPrice,
                            signal.TargetPrice,
                            entryAtr,
                            data.Bars[barIndex].Volume,
                            allocation.EffectiveEquity,
                            effectiveRisk,
                            sizing.PositionCapFraction,
                            customExit,
                            LongEntryFillPolicy.ResolveFallbackTargetMultiple(
                                entryDefinition.AtrStopMultiplier,
                                entryDefinition.AtrTargetMultiplier)));
                    }
                    else if (!isNextOpen)
                    {
                        openPositions[symbol] = BacktestOpenPositionFactory.CreateCurrentClose(
                            signal,
                            detector.PatternType,
                            entryDefinition?.Name,
                            data.Bars[barIndex],
                            barIndex,
                            sizing.Quantity,
                            entryAtr,
                            allocation.EffectiveEquity,
                            customExit);

                        context.RuntimeRegistry.RegisterEntry(
                            entryDefinition?.Name, context.TradingDay);
                    }

                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex, "{Symbol} 패턴 {Pattern} 감지 실패", symbol, detector.PatternType);
                }
            }
        }
    }
}

internal sealed record BacktestSignalEntryContext(
    DateTime Date,
    DateOnly TradingDay,
    int TimelineIndex,
    decimal InitialCapital,
    decimal RiskPerTrade,
    int MaxTotalPositions,
    int MaxWindow,
    IReadOnlyList<string> Symbols,
    IReadOnlyDictionary<string, PreparedSymbolData> SymbolData,
    IReadOnlyList<IPatternDetector> Detectors,
    MarketRegime Regime,
    WeightStrategy? WeightStrategy,
    BacktestPortfolioState Portfolio,
    BacktestStrategyRuntimeRegistry RuntimeRegistry,
    IReadOnlyList<TradeRecord> Trades,
    BacktestPendingEntryProcessor PendingEntries);

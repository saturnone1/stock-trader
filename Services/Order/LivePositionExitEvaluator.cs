using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Order;

public sealed record LivePositionExitDecision(LiveLongPositionExecutionIntent? Intent)
{
    public bool ShouldExit => Intent is not null;
    public string Reason => Intent?.Reason ?? string.Empty;
}

internal sealed record LiveCustomStrategyInstructions(
    StrategyExitInstruction? Exit,
    LongPositionScalingInstruction? Scaling);

/// <summary>
/// 실시간 포지션의 지표 스냅샷을 준비하고 공통 롱 포지션 정책으로 청산 여부를 평가합니다.
/// 백그라운드 워커는 조회 주기와 주문 조정만 담당합니다.
/// </summary>
public sealed class LivePositionExitEvaluator
{
    private readonly IIndicatorService _indicators;
    private readonly ICustomStrategyDetectorFactory _customDetectors;
    private readonly IOptionsMonitor<PatternSettings> _patternSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LivePositionExitEvaluator> _logger;

    public LivePositionExitEvaluator(
        IIndicatorService indicators,
        ICustomStrategyDetectorFactory customDetectors,
        IOptionsMonitor<PatternSettings> patternSettings,
        TimeProvider timeProvider,
        ILogger<LivePositionExitEvaluator> logger)
    {
        _indicators = indicators;
        _customDetectors = customDetectors;
        _patternSettings = patternSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<LivePositionExitDecision> EvaluateAsync(
        Position position,
        CompiledStrategy? customStrategy,
        IOhlcvRepository repository,
        PatternParameterOverrides? liveOverrides,
        CancellationToken ct = default,
        decimal currentEquity = 0m,
        int maxTotalPositions = 0)
    {
        var customPattern = customStrategy?.Source;
        var exitPolicy = customPattern == null
            ? LongPositionExitPolicyCatalog.ForPattern(position.PatternType, liveOverrides)
            : LongPositionExitPolicyCatalog.ForCustom(customPattern);
        var settings = liveOverrides == null
            ? _patternSettings.CurrentValue
            : PatternOverrideMerger.Merge(_patternSettings.CurrentValue, liveOverrides);
        var cumulativeRsi2 = settings.CumulativeRsi2;
        var tqqq = settings.Tqqq200Sma;
        List<OhlcvBar>? recentBars = null;
        decimal currentCumulativeRsi2 = 0;
        decimal currentCumulativeRsi2TrendMa = 0;
        decimal? dynamicStopFloor = null;
        var atr = position.EntryAtr;

        var needsBars = customPattern != null
            || position.PatternType == PatternType.CumulativeRsi2
            || position.PatternType == PatternType.Tqqq200Sma
            || atr <= 0
            || (exitPolicy.EnableTimeExit && exitPolicy.MaxHoldingBars > 0);
        if (needsBars)
        {
            var lookbackDays = position.PatternType == PatternType.Tqqq200Sma
                ? Math.Max(
                    StrategyEvaluationPolicy.LiveExitIndicatorLookbackDays,
                    Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(tqqq.SmaPeriod))
                : StrategyEvaluationPolicy.LiveExitIndicatorLookbackDays;
            var now = UtcNow;
            recentBars = (await repository.GetBarsAsync(
                    position.Symbol,
                    TimeFrame.Daily,
                    now.AddDays(-lookbackDays),
                    now,
                    ct))
                .OrderBy(bar => bar.Timestamp)
                .ToList();
            if (atr <= 0 && recentBars.Count >= StrategyEvaluationPolicy.EntryAtrPeriod + 1)
            {
                atr = CalculateSimpleAtr(
                    recentBars, StrategyEvaluationPolicy.EntryAtrPeriod);
            }
        }

        if (position.PatternType == PatternType.Tqqq200Sma
            && recentBars is { Count: > 0 }
            && tqqq.SmaPeriod > 0)
        {
            var closes = IndicatorService.ExtractCloses(recentBars.ToArray());
            var trendSma = _indicators.SMA(closes, tqqq.SmaPeriod);
            if (trendSma.Length > 0)
            {
                dynamicStopFloor = Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(
                    trendSma[^1], tqqq.SmaStopMultiplier);
            }
        }

        if (position.PatternType == PatternType.Tqqq200Sma && !dynamicStopFloor.HasValue)
        {
            _logger.LogWarning(
                "[EXIT-MGR] {Symbol}: TQQQ 장기 추세선 보호 손절을 계산하지 못했습니다. " +
                "기존 손절가를 유지합니다. bars={BarCount}, period={Period}",
                position.Symbol,
                recentBars?.Count ?? 0,
                tqqq.SmaPeriod);
        }

        if (position.PatternType == PatternType.CumulativeRsi2
            && recentBars is { Count: > 0 })
        {
            var closes = IndicatorService.ExtractCloses(recentBars.ToArray());
            var cumulative = _indicators.CumulativeRsi(
                closes, cumulativeRsi2.RsiPeriod, cumulativeRsi2.CumulativePeriod);
            var trend = _indicators.SMA(closes, cumulativeRsi2.LongTrendMaPeriod);
            if (cumulative.Length > 0) currentCumulativeRsi2 = cumulative[^1];
            if (trend.Length > 0) currentCumulativeRsi2TrendMa = trend[^1];
        }

        var customInstructions = await ResolveCustomInstructionsAsync(
            position, customStrategy, recentBars, repository,
            currentEquity, maxTotalPositions, ct);
        var strategyExit = position.PatternType == PatternType.CumulativeRsi2
            ? CumulativeRsi2ExitDecisionPolicy.Resolve(
                position.CurrentPrice,
                currentCumulativeRsi2,
                currentCumulativeRsi2TrendMa,
                cumulativeRsi2.ExitThreshold,
                cumulativeRsi2.LongTrendMaPeriod)
            : customInstructions.Exit;
        var stopDistance = Math.Abs(position.EntryPrice - position.StopLossPrice);
        if (stopDistance <= 0)
        {
            stopDistance = atr > 0
                ? atr
                : position.EntryPrice * StrategyEvaluationPolicy.FallbackRiskFraction;
        }
        if (position.InitialRiskDistance <= 0)
            position.InitialRiskDistance = stopDistance;

        var timeExitReached = recentBars is not null
            && HoldingPeriodPolicy.HasReachedDailyBarLimit(
                position.OpenedAt, recentBars, exitPolicy.MaxHoldingBars);
        var decision = LiveLongPositionExecutionAdapter.Evaluate(
            new LongPositionExecutionState(
                position.EntryPrice,
                position.StopLossPrice,
                position.TargetPrice,
                Math.Max(position.HighSinceEntry, position.EntryPrice),
                position.EntryPrice,
                position.InitialRiskDistance,
                position.EntryAtr > 0 ? position.EntryAtr : atr,
                EntryBarIndex: 0,
                position.Quantity,
                PartialProfitTaken: position.PartialProfitTaken,
                BreakevenApplied: position.BreakevenApplied,
                TrailingActivated: position.TrailingStopActivated),
            position.InitialQuantity > 0 ? position.InitialQuantity : position.Quantity,
            position.CurrentPrice,
            atr,
            exitPolicy,
            timeExitReached,
            strategyExit,
            dynamicStopFloor,
            customInstructions.Scaling,
            position.ScalingExecutionCounts);

        position.HighSinceEntry = decision.State.HighestPrice;
        position.StopLossPrice = decision.State.StopPrice;
        position.BreakevenApplied = decision.State.BreakevenApplied;
        position.TrailingStopActivated = decision.State.TrailingActivated;
        if (decision.StopUpdate is not null)
        {
            _logger.LogDebug(
                "[EXIT-MGR] {Symbol}: {Reason} {Price:F2}",
                position.Symbol,
                decision.StopUpdate.Reason,
                decision.StopUpdate.Price);
        }
        return new LivePositionExitDecision(decision.Intent);
    }

    private async Task<LiveCustomStrategyInstructions> ResolveCustomInstructionsAsync(
        Position position,
        CompiledStrategy? strategy,
        List<OhlcvBar>? recentBars,
        IOhlcvRepository repository,
        decimal currentEquity,
        int maxTotalPositions,
        CancellationToken ct)
    {
        if (strategy is null || recentBars is not { Count: >= StrategyEvaluationPolicy.MinimumWarmupBars })
            return new LiveCustomStrategyInstructions(null, null);

        var detector = _customDetectors.Create(strategy);
        detector.SetReferenceData(
            await LoadReferenceDataAsync(
                strategy, position.Symbol, recentBars, repository, ct),
            UtcNow);
        var bars = recentBars.ToArray();
        var exit = detector.HasExitRules && detector.ShouldExit(bars)
            ? new StrategyExitInstruction(
                position.CurrentPrice, $"{strategy.Name} 매도 조건 충족")
            : null;
        LongPositionScalingInstruction? scaling = null;
        if (detector.HasScalingRules)
        {
            var profitPercent = position.EntryPrice > 0
                ? (position.CurrentPrice - position.EntryPrice) / position.EntryPrice * 100m
                : 0m;
            var match = detector.EvaluateScaling(
                bars, profitPercent, position.ScalingExecutionCounts);
            if (match is not null)
            {
                var maxPositionCost = PositionScaleInCapacityPolicy.CalculateMaxPositionCost(
                    currentEquity,
                    maxTotalPositions,
                    strategy.PortfolioRules.MaxSinglePositionPercent);
                scaling = new LongPositionScalingInstruction(
                    match.RuleIndex,
                    match.Rule.Direction,
                    match.Rule.Percent,
                    maxPositionCost);
            }
        }
        return new LiveCustomStrategyInstructions(exit, scaling);
    }

    private async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceDataAsync(
        CompiledStrategy strategy,
        string symbol,
        List<OhlcvBar> symbolBars,
        IOhlcvRepository repository,
        CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = symbolBars.ToArray()
        };
        var now = UtcNow;
        foreach (var referenceSymbol in strategy.ReferenceSymbols.Where(
                     value => !value.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            result[referenceSymbol] = (await repository.GetBarsAsync(
                    referenceSymbol,
                    TimeFrame.Daily,
                    now.AddDays(-StrategyEvaluationPolicy.LiveExitIndicatorLookbackDays),
                    now,
                    ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    private static decimal CalculateSimpleAtr(List<OhlcvBar> bars, int period)
    {
        if (bars.Count < period + 1) return 0;
        return Enumerable.Range(bars.Count - period, period)
            .Select(index =>
            {
                var bar = bars[index];
                var previousClose = bars[index - 1].Close;
                return Math.Max(
                    bar.High - bar.Low,
                    Math.Max(
                        Math.Abs(bar.High - previousClose),
                        Math.Abs(bar.Low - previousClose)));
            })
            .Average();
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}

using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.StrategyPreview;

public sealed record PatternPreviewSimulationInput(
    string Symbol,
    TimeFrame TimeFrame,
    DateTime DataFrom,
    DateTime DataTo,
    OhlcvBar[] Bars,
    decimal[] Atr,
    IReadOnlyDictionary<string, OhlcvBar[]> ReferenceBars,
    OhlcvBar[] RegimeBars,
    ICompiledStrategyRuntime Runtime);

/// <summary>
/// 준비된 시세와 컴파일된 전략 런타임으로 단일 종목 미리보기를 재생합니다.
/// HTTP, 저장소, 데이터 제공자, 시스템 시계에 의존하지 않습니다.
/// </summary>
public sealed class PatternPreviewSimulationEngine
{
    private const int PreviewQuantity = 100;

    public async Task<PatternPreviewResult?> RunAsync(
        PatternPreviewSimulationInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Bars.Length != input.Atr.Length)
            throw new ArgumentException("ATR 배열은 시세 배열과 길이가 같아야 합니다.", nameof(input));

        var requestedStartIndex = Array.FindIndex(
            input.Bars, bar => bar.Timestamp >= input.DataFrom);
        if (requestedStartIndex < 0) return null;

        var visibleBars = input.Bars
            .Skip(requestedStartIndex)
            .Where(bar => bar.Timestamp < input.DataTo)
            .ToArray();
        if (visibleBars.Length == 0) return null;

        var strategy = input.Runtime.Strategy;
        var circuitBreaker = strategy.CircuitBreaker;
        var reentry = strategy.Reentry;
        var portfolioRules = strategy.PortfolioRules;
        var markers = new List<PatternPreviewMarker>();
        var matches = new List<PatternPreviewMarker>();
        var warnings = input.ReferenceBars
            .Where(pair => pair.Value.Length < StrategyEvaluationPolicy.MinimumWarmupBars)
            .Select(pair => $"참조 종목 {pair.Key} 데이터가 부족해 해당 조건은 미리보기에서 제한될 수 있습니다.")
            .Distinct()
            .ToList();
        if (MarketRegimeTrendPolicy.IsUnknown(
                MarketRegimeTrendPolicy.Evaluate(input.RegimeBars, visibleBars[0].Timestamp)))
        {
            warnings.Add(MarketRegimeTrendPolicy.InsufficientHistoryWarning);
        }
        var runtimeReferenceData = input.ReferenceBars.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var evaluationStartIndex = Math.Max(
            StrategyEvaluationPolicy.MinimumWarmupBars - 1,
            requestedStartIndex);
        var evaluationEndIndex = Array.FindIndex(
            input.Bars, bar => bar.Timestamp >= input.DataTo);
        if (evaluationEndIndex < 0) evaluationEndIndex = input.Bars.Length;
        PreviewPosition? position = null;
        decimal compoundedReturn = 1m;
        var completedTrades = 0;
        var winningTrades = 0;
        var tradeTransition = new StrategyTradeTransitionState();
        var drawdown = new StrategyDrawdownState(PeakEquity: 1m);
        var currentEntryDay = DateOnly.MinValue;
        var entriesToday = 0;
        var safetyBlockedEntries = 0;
        var exitPolicy = LongPositionExitPolicyCatalog.ForCustom(strategy.Source);

        void Complete(PreviewPosition openPosition)
        {
            var cycleReturn = openPosition.InvestedCapital > 0
                ? openPosition.RealizedPnl / openPosition.InvestedCapital
                : 0;
            compoundedReturn *= Math.Max(0m, 1m + cycleReturn * openPosition.AllocationScale);
            completedTrades++;
            if (openPosition.RealizedPnl > 0) winningTrades++;
        }

        for (var index = evaluationStartIndex; index < evaluationEndIndex; index++)
        {
            ct.ThrowIfCancellationRequested();
            var current = input.Bars[index];
            var window = input.Bars[..(index + 1)];
            input.Runtime.SetReferenceData(runtimeReferenceData, current.Timestamp);

            if (position is not null && index >= position.EntryIndex)
            {
                var instructions = CompiledStrategyPositionInstructionResolver.Resolve(
                    input.Runtime,
                    window,
                    current.Close,
                    position.EntryPrice,
                    position.ScaleCounts);
                var scaling = instructions.Scaling;

                var result = LongPositionExecutionSessionPolicy.Evaluate(
                    position.ToSessionState(),
                    current,
                    index,
                    input.Atr[index],
                    exitPolicy,
                    instructions.Exit,
                    scaling: scaling);

                position.Apply(result.State);

                foreach (var executionEvent in result.Events)
                {
                    if (executionEvent.Type == LongPositionSessionEventType.StopMoved)
                    {
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            "STOP_MOVE",
                            executionEvent.Price,
                            Details: executionEvent.Reason));
                    }
                    else if (executionEvent.Type == LongPositionSessionEventType.PartialExit)
                    {
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            "PARTIAL_EXIT",
                            executionEvent.Price,
                            Details: $"보유 수량의 {executionEvent.Quantity * 100m / (executionEvent.Quantity + executionEvent.QuantityAfter):F0}% 매도",
                            Reason: executionEvent.Reason));
                    }
                    else if (executionEvent.Type == LongPositionSessionEventType.ScaleIn)
                    {
                        position.InvestedCapital += current.Close * executionEvent.Quantity;
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            StrategyCatalog.ScalingInDirection,
                            current.Close,
                            Details: $"최초 수량의 {scaling!.Percent:F0}% 추가 매수"
                                + $"({executionEvent.Quantity}주) · 새 평균가 {executionEvent.EntryPriceAfter:F2}"));
                    }
                    else if (executionEvent.Type == LongPositionSessionEventType.ScaleOut)
                    {
                        markers.Add(new PatternPreviewMarker(
                            current.Timestamp,
                            StrategyCatalog.ScalingOutDirection,
                            current.Close,
                            Details: $"최초 수량의 {scaling!.Percent:F0}% 일부 매도"
                                + $"({executionEvent.Quantity}주)"));
                    }
                }

                var policyExit = result.Events.LastOrDefault(
                    item => item.Type == LongPositionSessionEventType.Exit);
                if (result.IsClosed && policyExit is not null)
                {
                    Complete(position);
                    tradeTransition = StrategyTradeTransitionPolicy.Apply(
                        tradeTransition,
                        new StrategyTradeTransitionRequest(
                            index,
                            index,
                            position.RealizedPnl,
                            reentry,
                            circuitBreaker));
                    drawdown = StrategyDrawdownPolicy.Observe(
                        drawdown,
                        compoundedReturn,
                        circuitBreaker.MaxDrawdownPercent);

                    markers.Add(new PatternPreviewMarker(
                        current.Timestamp, "EXIT", policyExit.Price, Reason: policyExit.Reason));
                    position = null;
                }
            }

            var regime = MarketRegimeTrendPolicy.Evaluate(
                input.RegimeBars, current.Timestamp);
            var signal = await input.Runtime.EvaluateEntryAsync(
                input.Symbol, window, regime, ct);
            if (signal is null) continue;

            matches.Add(new PatternPreviewMarker(
                current.Timestamp,
                "MATCH",
                current.Close,
                signal.StopLossPrice,
                signal.TargetPrice,
                signal.Details));
            if (position is not null) continue;

            var signalDay = DateOnly.FromDateTime(current.Timestamp);
            if (signalDay != currentEntryDay)
            {
                currentEntryDay = signalDay;
                entriesToday = 0;
            }

            var entryEligibility = StrategyEntryEligibilityPolicy.Evaluate(
                new StrategyEntryEligibilityRequest(
                    DefaultMaxPositions: 1,
                    StrategyMaxPositions: portfolioRules.MaxTotalPositions,
                    OpenPositionCount: 0,
                    DrawdownBlocked: drawdown.IsBlocked,
                    ConsecutiveLossBlocked:
                        index < tradeTransition.CircuitBreakerBlockedUntilStep,
                    MaxEntriesPerSession: portfolioRules.MaxEntriesPerDay,
                    EntriesThisSession: entriesToday,
                    ReentryBlocked: index < tradeTransition.ReentryBlockedUntilStep));
            if (!entryEligibility.CanEnter)
            {
                safetyBlockedEntries++;
                continue;
            }

            var entryIndex = index;
            var entryPrice = current.Close;
            var entryDate = current.Timestamp;
            if (string.Equals(
                    strategy.EntryMode,
                    StrategyCatalog.NextOpenEntryMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= evaluationEndIndex) continue;
                entryIndex = index + 1;
                entryPrice = input.Bars[entryIndex].Open;
                entryDate = input.Bars[entryIndex].Timestamp;
            }

            var fallbackTargetMultiple = strategy.Source.AtrStopMultiplier > 0
                ? strategy.Source.AtrTargetMultiplier / strategy.Source.AtrStopMultiplier
                : 1m;
            var fill = LongEntryFillPolicy.Reprice(
                signal.EntryPrice,
                signal.StopLossPrice,
                signal.TargetPrice,
                entryPrice,
                fallbackTargetMultiple);
            if (fill is null) continue;

            var entryAtr = input.Atr[index];
            position = new PreviewPosition
            {
                EntryIndex = entryIndex,
                EntryPrice = fill.EntryPrice,
                StopPrice = fill.StopPrice,
                TargetPrice = fill.TargetPrice,
                HighestPrice = fill.EntryPrice,
                LowestPrice = fill.EntryPrice,
                InitialRisk = fill.RiskDistance,
                EntryAtr = entryAtr > 0 ? entryAtr : fill.RiskDistance,
                InvestedCapital = fill.EntryPrice * PreviewQuantity,
                TotalCost = fill.EntryPrice * PreviewQuantity,
                AllocationScale = Math.Min(
                    PositionAllocationPolicy.NormalizeScale(signal.AllocationScale),
                    portfolioRules.MaxSinglePositionPercent > 0
                        ? portfolioRules.MaxSinglePositionPercent / 100m
                        : 1m)
            };
            entriesToday++;
            markers.Add(new PatternPreviewMarker(
                entryDate,
                "ENTRY",
                fill.EntryPrice,
                fill.StopPrice,
                fill.TargetPrice,
                $"{signal.Details} · 매수 비중 {signal.AllocationScale * 100m:F0}%"));
        }

        var displayStart = visibleBars[0].Timestamp;
        var visibleMarkers = markers
            .Where(marker => marker.Date >= displayStart && marker.Date < input.DataTo)
            .ToArray();
        var visibleMatches = matches
            .Where(marker => marker.Date >= displayStart && marker.Date < input.DataTo)
            .ToArray();
        var completedReturnPercent = (compoundedReturn - 1m) * 100m;
        var totalReturnPercent = completedReturnPercent;
        decimal? openPositionReturnPercent = null;
        if (position is not null)
        {
            var lastVisibleClose = visibleBars[^1].Close;
            var openPnl = position.RealizedPnl
                + (lastVisibleClose - position.EntryPrice) * position.CurrentQuantity;
            var openCycleReturn = position.InvestedCapital > 0
                ? openPnl / position.InvestedCapital
                : 0;
            openPositionReturnPercent = openCycleReturn * 100m;
            totalReturnPercent = (compoundedReturn
                * (1m + openCycleReturn * position.AllocationScale) - 1m) * 100m;
        }

        return new PatternPreviewResult(
            input.Symbol,
            input.TimeFrame,
            TimeFrameCatalog.IsIntraday(input.TimeFrame),
            input.DataFrom,
            input.DataTo,
            visibleBars,
            visibleMarkers,
            visibleMatches,
            new PatternPreviewSummary(
                visibleMatches.Length,
                visibleMarkers.Count(marker => marker.Type == "ENTRY"),
                visibleMarkers.Count(marker => marker.Type == "EXIT"),
                visibleMarkers.Count(marker => marker.Type == StrategyCatalog.ScalingInDirection),
                visibleMarkers.Count(marker =>
                    marker.Type is StrategyCatalog.ScalingOutDirection or "PARTIAL_EXIT"),
                visibleMarkers.Count(marker => marker.Type == "STOP_MOVE"),
                safetyBlockedEntries,
                completedTrades,
                winningTrades,
                completedTrades > 0 ? (decimal)winningTrades / completedTrades : 0,
                completedReturnPercent,
                totalReturnPercent,
                openPositionReturnPercent,
                position is not null,
                displayStart,
                visibleBars[^1].Timestamp,
                visibleBars.Length,
                visibleBars.Length),
            warnings);
    }

    private sealed class PreviewPosition
    {
        public int EntryIndex { get; init; }
        public decimal EntryPrice { get; set; }
        public decimal StopPrice { get; set; }
        public decimal TargetPrice { get; init; }
        public decimal HighestPrice { get; set; }
        public decimal LowestPrice { get; set; }
        public decimal InitialRisk { get; init; }
        public decimal EntryAtr { get; init; }
        public int InitialQuantity { get; init; } = PreviewQuantity;
        public int CurrentQuantity { get; set; } = PreviewQuantity;
        public bool PartialProfitTaken { get; set; }
        public bool BreakevenApplied { get; set; }
        public bool TrailingActivated { get; set; }
        public decimal InvestedCapital { get; set; }
        public decimal TotalCost { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal AllocationScale { get; init; } = 1m;
        public Dictionary<int, int> ScaleCounts { get; private set; } = [];

        public LongPositionSessionState ToSessionState() => new(
            new LongPositionExecutionState(
                EntryPrice,
                StopPrice,
                TargetPrice,
                HighestPrice,
                LowestPrice,
                InitialRisk,
                EntryAtr,
                EntryIndex,
                CurrentQuantity,
                PartialProfitTaken,
                BreakevenApplied,
                TrailingActivated),
            InitialQuantity,
            TotalCost,
            RealizedPnl,
            ScaleCounts);

        public void Apply(LongPositionSessionState state)
        {
            EntryPrice = state.Execution.EntryPrice;
            StopPrice = state.Execution.StopPrice;
            HighestPrice = state.Execution.HighestPrice;
            LowestPrice = state.Execution.LowestPrice;
            CurrentQuantity = state.Execution.CurrentQuantity;
            TotalCost = state.TotalCost;
            RealizedPnl = state.RealizedPnl;
            PartialProfitTaken = state.Execution.PartialProfitTaken;
            BreakevenApplied = state.Execution.BreakevenApplied;
            TrailingActivated = state.Execution.TrailingActivated;
            ScaleCounts = new Dictionary<int, int>(state.ScalingExecutionCounts);
        }
    }
}

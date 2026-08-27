using System.Text.Json;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Engine.Indicators;
using StockTrader.Engine.MarketData;
using StockTrader.Engine.Strategies;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public sealed record TradingPositionEvaluation(
    decimal CurrentPrice,
    decimal HighSinceEntry,
    decimal StopLossPrice,
    decimal InitialRiskDistance,
    decimal EntryAtr,
    bool BreakevenApplied,
    bool TrailingStopActivated,
    string? Action,
    int Quantity,
    string? Reason,
    int? ScalingRuleIndex,
    bool MarksPartialProfit,
    DateTime EvaluatedThroughBarUtc);

/// <summary>Pure financial policy evaluation over immutable execution artifacts and completed bars.</summary>
public static class TradingPositionEvaluator
{
    private const int AtrPeriod = 14;
    private const decimal FallbackRiskFraction = 0.02m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IndicatorCalculator Indicators = new();

    public static TradingPositionEvaluation Evaluate(
        TradingPositionProjection position,
        MarketDataExecutionWindowResponse primarySeries,
        IReadOnlyDictionary<string, MarketDataExecutionWindowResponse>? referenceSeries,
        decimal currentEquity,
        int maxTotalPositions)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(primarySeries);
        var artifact = position.ExecutionContext?.ExecutionArtifact
            ?? throw new InvalidOperationException("position-execution-context-missing");
        var management = artifact.PositionManagement
            ?? throw new InvalidOperationException("position-management-artifact-missing");
        if (TradingExecutionArtifactPolicy.Error(artifact) is { } compatibilityError)
            throw new InvalidOperationException(compatibilityError);
        if (!primarySeries.Evidence.IsComplete || primarySeries.Bars.Count == 0)
            throw new InvalidOperationException("incomplete-position-market-data-evidence");
        if (primarySeries.PriorEvaluatedRangeCorrected)
            throw new InvalidOperationException("position-market-data-correction-requires-reconciliation");
        if (!string.Equals(primarySeries.Evidence.Symbol, position.Symbol,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("position-market-data-symbol-mismatch");

        var bars = primarySeries.Bars.Select(Map).OrderBy(value => value.Timestamp).ToArray();
        if (position.LastEvaluatedBarUtc is { } previous
            && previous < bars[0].Timestamp)
            throw new InvalidOperationException("position-evaluation-history-gap");
        var startIndex = position.LastEvaluatedBarUtc is { } evaluated
            ? Array.FindIndex(bars, value => value.Timestamp > evaluated)
            : Array.FindIndex(bars, value => value.Timestamp >= position.OpenedAtUtc);
        if (startIndex < 0)
            throw new InvalidOperationException("no-unevaluated-completed-position-bars");
        var entryBarIndex = Array.FindIndex(bars, value => value.Timestamp >= position.OpenedAtUtc);
        if (entryBarIndex < 0) entryBarIndex = startIndex;
        var initialAtr = position.EntryAtr;
        var risk = position.InitialRiskDistance > 0
            ? position.InitialRiskDistance
            : Math.Abs(position.EntryPrice - position.StopLossPrice);
        var policy = Map(management.ExitPolicy);
        var strategy = artifact.Kind == TradingExecutionArtifactKinds.StrategyDocument
            ? Compile(artifact) : null;
        var runtime = strategy is null ? null : new CompiledPositionRuleRuntime(strategy);
        var references = BuildReferences(position.Symbol, bars, referenceSeries);
        var counts = position.ScalingExecutions.ToDictionary(
            value => value.RuleIndex, value => value.ExecutionCount);
        var state = new LongPositionExecutionState(
            position.EntryPrice, position.StopLossPrice, position.TargetPrice,
            Math.Max(position.HighSinceEntry, position.EntryPrice), position.EntryPrice,
            risk, initialAtr, entryBarIndex, position.Quantity, position.PartialProfitTaken,
            position.BreakevenApplied, position.TrailingStopActivated);

        for (var index = startIndex; index < bars.Length; index++)
        {
            var current = bars[index];
            var prefix = bars[..(index + 1)];
            var closes = prefix.Select(value => value.Close).ToArray();
            var atr = Indicators.ATR(prefix, AtrPeriod)[^1];
            if (initialAtr <= 0 && atr > 0) initialAtr = atr;
            if (risk <= 0)
                risk = initialAtr > 0 ? initialAtr : position.EntryPrice * FallbackRiskFraction;
            state = state with { RiskDistance = risk, EntryAtr = initialAtr };

            StrategyExitInstruction? strategyExit = null;
            decimal? dynamicStopFloor = null;
            LongPositionScalingInstruction? scaling = null;
            if (management.CumulativeRsiExit is { } cumulative)
            {
                var cumulativeValues = Indicators.CumulativeRsi(
                    closes, cumulative.RsiPeriod, cumulative.CumulativePeriod);
                var trend = Indicators.SMA(closes, cumulative.LongTrendMovingAveragePeriod);
                if (trend[^1] > 0 && current.Close <= trend[^1])
                    strategyExit = new(current.Close,
                        $"{cumulative.LongTrendMovingAveragePeriod}SMA 이탈");
                else if (cumulativeValues[^1] >= cumulative.ExitThreshold)
                    strategyExit = new(current.Close, $"누적 RSI 청산({cumulativeValues[^1]:F1})");
            }
            if (management.TrendStop is { } trendStop)
            {
                var trend = Indicators.SMA(closes, trendStop.MovingAveragePeriod);
                if (trend[^1] > 0 && trendStop.StopMultiplier > 0)
                    dynamicStopFloor = trend[^1] * trendStop.StopMultiplier;
            }
            if (runtime is not null && strategy is not null)
            {
                if (runtime.ShouldExit(prefix, references, current.Timestamp))
                    strategyExit = new(current.Close, LongPositionExecutionReasons.StrategyRuleExit);
                var profitPercent = position.EntryPrice > 0
                    ? (current.Close - position.EntryPrice) / position.EntryPrice * 100m : 0m;
                var match = currentEquity > 0 && maxTotalPositions > 0
                    ? runtime.EvaluateScaling(
                        prefix, profitPercent, counts, references, current.Timestamp)
                    : null;
                if (match is { } value)
                {
                    var maxCost = strategy.PortfolioRules.MaxSinglePositionPercent > 0
                        ? currentEquity * strategy.PortfolioRules.MaxSinglePositionPercent / 100m
                        : decimal.MaxValue;
                    scaling = new(
                        value.RuleIndex, value.Rule.Direction, value.Rule.Percent, maxCost);
                }
            }

            var session = LongPositionExecutionSessionPolicy.Evaluate(
                new LongPositionSessionState(
                    state,
                    position.InitialQuantity > 0 ? position.InitialQuantity : position.Quantity,
                    state.EntryPrice * state.CurrentQuantity, 0m, counts),
                current, index, atr, policy, strategyExit, dynamicStopFloor, scaling);
            var execution = session.Events.FirstOrDefault(value => value.Type is
                LongPositionSessionEventType.PartialExit or LongPositionSessionEventType.Exit
                or LongPositionSessionEventType.ScaleIn or LongPositionSessionEventType.ScaleOut);
            state = session.State.Execution;
            if (execution is not null)
            {
                // Financial quantity/average changes only after durable broker evidence.
                state = state with
                {
                    EntryPrice = position.EntryPrice,
                    CurrentQuantity = position.Quantity,
                    PartialProfitTaken = position.PartialProfitTaken,
                };
                return Result(current, state, risk, initialAtr, execution);
            }
        }
        return Result(bars[^1], state, risk, initialAtr, null);
    }

    private static TradingPositionEvaluation Result(
        PriceBar bar,
        LongPositionExecutionState state,
        decimal risk,
        decimal entryAtr,
        LongPositionSessionEvent? execution) => new(
            bar.Close, state.HighestPrice, state.StopPrice, risk, entryAtr,
            state.BreakevenApplied, state.TrailingActivated,
            execution is null ? null : Action(execution.Type),
            execution?.Quantity ?? 0, execution?.Reason, execution?.ScalingRuleIndex,
            execution?.Type == LongPositionSessionEventType.PartialExit,
            bar.Timestamp);

    private static CompiledStrategy Compile(TradingStrategyExecutionArtifact artifact)
    {
        var source = artifact.StrategyDocument
            ?? throw new InvalidOperationException("strategy-document-artifact-missing");
        var document = JsonSerializer.Deserialize<StrategyDocument>(source.StrategyDocumentJson, Json)
            ?? throw new InvalidOperationException("empty-strategy-document-artifact");
        var result = StrategyCompiler.Compile(document);
        return result.Strategy ?? throw new InvalidOperationException(string.Join(" ", result.Errors));
    }

    public static IReadOnlyList<string> ReferenceSymbols(
        TradingStrategyExecutionArtifact artifact)
    {
        if (artifact.Kind != TradingExecutionArtifactKinds.StrategyDocument) return [];
        return Compile(artifact).ReferenceSymbols.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyDictionary<string, PriceBar[]> BuildReferences(
        string symbol, PriceBar[] primary,
        IReadOnlyDictionary<string, MarketDataExecutionWindowResponse>? series)
    {
        var result = new Dictionary<string, PriceBar[]>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = primary,
        };
        if (series is not null)
            foreach (var pair in series)
            {
                if (!pair.Value.Evidence.IsComplete)
                    throw new InvalidOperationException("incomplete-reference-market-data-evidence");
                if (pair.Value.PriorEvaluatedRangeCorrected)
                    throw new InvalidOperationException(
                        "reference-market-data-correction-requires-reconciliation");
                result[pair.Key] = pair.Value.Bars.Select(Map).OrderBy(value => value.Timestamp).ToArray();
            }
        return result;
    }

    private static LongPositionExitPolicy Map(TradingLongPositionPolicy value) => new(
        value.MaxHoldingBars, value.EnableTrailingStop, value.TrailingStopAtrMultiplier,
        value.TrailingActivationR, value.EnablePartialProfit, value.PartialProfitRMultiple,
        value.EnableTargetExit, value.EnableTimeExit, value.BreakevenAtrMultiplier,
        value.StopReason, value.ProtectedStopReason);

    private static PriceBar Map(MarketDataBar value) => new(
        value.TimestampUtc, Enum.Parse<StockTrader.Domain.MarketData.TimeFrame>(value.TimeFrame, true),
        value.Open, value.High, value.Low, value.Close, value.Volume, value.Vwap);

    private static string Action(LongPositionSessionEventType type) => type switch
    {
        LongPositionSessionEventType.PartialExit => TradingPositionActionKinds.PartialExit,
        LongPositionSessionEventType.ScaleIn => TradingPositionActionKinds.ScaleIn,
        LongPositionSessionEventType.ScaleOut => TradingPositionActionKinds.ScaleOut,
        _ => TradingPositionActionKinds.FullExit,
    };
}

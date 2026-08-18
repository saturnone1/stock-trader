using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 보유 포지션에 장중 체결 정책, 종가 규칙 청산, 추가 매수와 일부 매도를 순서대로 적용합니다.
/// </summary>
internal sealed class BacktestPositionExitProcessor
{
    public void Process(BacktestPositionExitContext context)
    {
        var openPositions = context.Portfolio.OpenPositions;
        foreach (var symbol in openPositions.Keys.ToList())
        {
            if (!context.SymbolData.TryGetValue(symbol, out var data)) continue;
            if (!data.TimestampToIndex.TryGetValue(context.Date, out var barIndex)) continue;

            var position = openPositions[symbol];
            var detector = context.RuntimeRegistry.FindDetector(position.CustomPatternName);

            var tradesBefore = context.TradeLedger.Count;
            var exitResult = context.Simulator.ProcessExitLogic(
                position, data.Bars[barIndex], barIndex,
                data.Atr[barIndex], data.TqqqProtectiveStopFloor[barIndex],
                data.CumulativeRsi2[barIndex], data.CumulativeRsi2TrendMa[barIndex],
                context.CumulativeRsi2Config,
                context.ExitPolicies,
                context.ExitOverrides,
                symbol,
                context.TradeLedger.Trades);
            context.TradeLedger.SettleSince(tradesBefore);

            if (exitResult == null)
            {
                ClosePositionState(symbol, position, barIndex, context, tradesBefore);
                continue;
            }

            position = exitResult;
            openPositions[symbol] = position;
            var windowSize = Math.Min(barIndex + 1, context.MaxWindow);
            var windowStart = barIndex + 1 - windowSize;
            var windowBars = data.Bars[windowStart..(barIndex + 1)];

            // 장중 스탑/목표가 평가가 끝난 뒤 종가 기반 사용자 청산 규칙을 적용한다.
            if (detector is { HasExitRules: true } && detector.ShouldExit(windowBars))
            {
                tradesBefore = context.TradeLedger.Count;
                context.TradeLedger.Trades.Add(BacktestExecutionAdapter.CreateTradeRecord(
                    symbol,
                    position,
                    data.Bars[barIndex].Close,
                    data.Bars[barIndex].Timestamp,
                    "규칙 청산",
                    CurrentQuantity(position)));
                context.TradeLedger.SettleSince(tradesBefore);
                ClosePositionState(symbol, position, barIndex, context, tradesBefore);
                continue;
            }

            // 추가 매수/일부 매도는 종가에서만 실행해 같은 봉의 과거 고가·저가에 소급하지 않는다.
            if (detector is not { HasScalingRules: true }) continue;

            var currentProfitPercent = position.EntryPrice > 0
                ? (data.Bars[barIndex].Close - position.EntryPrice) / position.EntryPrice * 100
                : 0;
            var scalingMatch = detector.EvaluateScaling(
                windowBars, currentProfitPercent, position.ScaleCounts);
            if (scalingMatch == null) continue;
            var scaling = scalingMatch.Rule;

            var initialQuantity = position.InitialQuantity > 0
                ? position.InitialQuantity
                : position.Quantity;
            var close = data.Bars[barIndex].Close;
            var maxScaleInQuantity = ResolveMaxScaleInQuantity(
                position,
                close,
                context.RuntimeRegistry.Find(position.CustomPatternName),
                context);
            var scalingDecision = LongPositionScalingPolicy.Apply(
                new LongPositionScalingState(
                    initialQuantity,
                    CurrentQuantity(position),
                    position.EntryPrice,
                    position.TotalCost),
                scaling.Direction,
                scaling.Percent,
                close,
                maxScaleInQuantity);
            if (scalingDecision is null) continue;

            LongPositionScalingPolicy.RegisterExecution(
                position.ScaleCounts, scalingMatch.RuleIndex);

            if (scalingDecision.Action == LongPositionScalingAction.ScaleIn)
            {
                ApplyScalingState(position, scalingDecision.State);
                continue;
            }

            tradesBefore = context.TradeLedger.Count;
            context.TradeLedger.Trades.Add(BacktestExecutionAdapter.CreateTradeRecord(
                symbol,
                position,
                close,
                data.Bars[barIndex].Timestamp,
                $"분할 매도({scaling.Percent}%)",
                scalingDecision.ExecutedQuantity));
            ApplyScalingState(position, scalingDecision.State);
            context.TradeLedger.SettleSince(tradesBefore);
        }
    }

    private void ClosePositionState(
        string symbol,
        BacktestExecutionAdapter.OpenPosition position,
        int barIndex,
        BacktestPositionExitContext context,
        int tradesBefore)
    {
        context.Portfolio.OpenPositions.Remove(symbol);
        if (context.TradeLedger.Count <= tradesBefore) return;

        context.RuntimeRegistry.RegisterClosedTrade(
            position.CustomPatternName,
            symbol,
            barIndex,
            context.TimelineIndex,
            context.TradeLedger.Trades[^1]);
    }

    private static int ResolveMaxScaleInQuantity(
        BacktestExecutionAdapter.OpenPosition position,
        decimal close,
        BacktestStrategyRuntime? runtime,
        BacktestPositionExitContext context)
    {
        var capFraction = context.MaxTotalPositions > 0
            ? 1m / context.MaxTotalPositions
            : 0.10m;
        if (runtime?.Portfolio.MaxSinglePositionPercent > 0)
            capFraction = Math.Min(capFraction, runtime.Portfolio.MaxSinglePositionPercent / 100m);

        var remainingCapital = Math.Max(
            0m,
            context.Portfolio.CurrentEquity * capFraction - position.TotalCost);
        return LongPositionSizingPolicy.CalculateAffordableQuantity(remainingCapital, close);
    }

    private static void ApplyScalingState(
        BacktestExecutionAdapter.OpenPosition position,
        LongPositionScalingState state)
    {
        position.CurrentQuantity = state.CurrentQuantity;
        position.TotalCost = state.TotalCost;
        position.EntryPrice = state.EntryPrice;
    }

    private static int CurrentQuantity(BacktestExecutionAdapter.OpenPosition position) =>
        position.CurrentQuantity > 0 ? position.CurrentQuantity : position.Quantity;
}

internal sealed record BacktestPositionExitContext(
    DateTime Date,
    int TimelineIndex,
    IReadOnlyDictionary<string, PreparedSymbolData> SymbolData,
    int MaxWindow,
    int MaxTotalPositions,
    CumulativeRsi2Config CumulativeRsi2Config,
    Dictionary<PatternType, LongPositionExitPolicy> ExitPolicies,
    PatternParameterOverrides? ExitOverrides,
    BacktestPortfolioState Portfolio,
    BacktestStrategyRuntimeRegistry RuntimeRegistry,
    BacktestTradeLedger TradeLedger,
    BacktestExecutionAdapter Simulator);

using StockTrader.Application.Backtesting;
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
    private readonly Dictionary<string, Dictionary<int, int>> _positionScaleCounts =
        new(StringComparer.OrdinalIgnoreCase);

    public void Process(BacktestPositionExitContext context)
    {
        var openPositions = context.Portfolio.OpenPositions;
        foreach (var symbol in openPositions.Keys.ToList())
        {
            if (!context.SymbolData.TryGetValue(symbol, out var data)) continue;
            if (!data.TimestampToIndex.TryGetValue(context.Date, out var barIndex)) continue;

            var position = openPositions[symbol];
            var detector = position.CustomPatternName != null
                && context.DetectorsByName.TryGetValue(position.CustomPatternName, out var matchedDetector)
                    ? matchedDetector
                    : null;
            var runtime = position.CustomPatternName != null
                && context.StrategyRuntimes.TryGetValue(position.CustomPatternName, out var matchedRuntime)
                    ? matchedRuntime
                    : null;

            var tradesBefore = context.Trades.Count;
            var exitResult = context.Simulator.ProcessExitLogic(
                position, data.Bars[barIndex], barIndex,
                data.Atr[barIndex], data.Sma200[barIndex],
                data.CumulativeRsi2[barIndex], data.CumulativeRsi2TrendMa[barIndex],
                context.CumulativeRsi2Config,
                context.PatternExitProfiles,
                context.ExitOverrides,
                symbol,
                context.Trades);
            context.ApplyNewTradeCosts(tradesBefore);

            if (exitResult == null)
            {
                ClosePositionState(symbol, position, barIndex, runtime, context, tradesBefore);
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
                tradesBefore = context.Trades.Count;
                context.Trades.Add(TradeSimulator.CreateTradeRecord(
                    symbol,
                    position,
                    data.Bars[barIndex].Close,
                    data.Bars[barIndex].Timestamp,
                    "규칙 청산",
                    CurrentQuantity(position)));
                context.ApplyNewTradeCosts(tradesBefore);
                ClosePositionState(symbol, position, barIndex, runtime, context, tradesBefore);
                continue;
            }

            // 추가 매수/일부 매도는 종가에서만 실행해 같은 봉의 과거 고가·저가에 소급하지 않는다.
            if (detector is not { HasScalingRules: true }) continue;

            var currentProfitPercent = position.EntryPrice > 0
                ? (data.Bars[barIndex].Close - position.EntryPrice) / position.EntryPrice * 100
                : 0;
            if (!_positionScaleCounts.TryGetValue(symbol, out var scaleCounts))
            {
                scaleCounts = [];
                _positionScaleCounts[symbol] = scaleCounts;
            }

            var scaling = detector.CheckScaling(windowBars, currentProfitPercent, scaleCounts);
            if (scaling == null) continue;

            var scaleQuantity = Math.Max(1, (int)(position.Quantity * scaling.Percent / 100m));
            if (scaling.Direction == StrategyCatalog.ScalingInDirection)
            {
                ApplyScaleIn(position, data.Bars[barIndex].Close, scaleQuantity, runtime, context);
                continue;
            }

            var sellQuantity = Math.Min(scaleQuantity, CurrentQuantity(position) - 1);
            if (sellQuantity <= 0) continue;

            tradesBefore = context.Trades.Count;
            context.Trades.Add(TradeSimulator.CreateTradeRecord(
                symbol,
                position,
                data.Bars[barIndex].Close,
                data.Bars[barIndex].Timestamp,
                $"분할 매도({scaling.Percent}%)",
                sellQuantity));
            position.CurrentQuantity = CurrentQuantity(position) - sellQuantity;
            position.TotalCost = position.EntryPrice * position.CurrentQuantity;
            context.ApplyNewTradeCosts(tradesBefore);
        }
    }

    private void ClosePositionState(
        string symbol,
        TradeSimulator.OpenPosition position,
        int barIndex,
        BacktestStrategyRuntime? runtime,
        BacktestPositionExitContext context,
        int tradesBefore)
    {
        context.Portfolio.OpenPositions.Remove(symbol);
        _positionScaleCounts.Remove(symbol);
        if (runtime == null || context.Trades.Count <= tradesBefore) return;

        BacktestStrategyTransitionPolicy.RegisterClosedTrade(
            $"{position.CustomPatternName}|{symbol}",
            barIndex,
            context.TimelineIndex,
            context.Trades[^1],
            runtime,
            context.ReentryCooldowns);
    }

    private static void ApplyScaleIn(
        TradeSimulator.OpenPosition position,
        decimal close,
        int requestedQuantity,
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
        var affordableQuantity = close > 0 ? (int)(remainingCapital / close) : 0;
        var scaleQuantity = Math.Min(requestedQuantity, affordableQuantity);
        if (scaleQuantity <= 0) return;

        var currentQuantity = CurrentQuantity(position);
        var newQuantity = currentQuantity + scaleQuantity;
        var newTotalCost = position.TotalCost + close * scaleQuantity;
        position.CurrentQuantity = newQuantity;
        position.TotalCost = newTotalCost;
        position.EntryPrice = newTotalCost / newQuantity;
    }

    private static int CurrentQuantity(TradeSimulator.OpenPosition position) =>
        position.CurrentQuantity > 0 ? position.CurrentQuantity : position.Quantity;
}

internal sealed record BacktestPositionExitContext(
    DateTime Date,
    int TimelineIndex,
    IReadOnlyDictionary<string, PreparedSymbolData> SymbolData,
    int MaxWindow,
    int MaxTotalPositions,
    CumulativeRsi2Config CumulativeRsi2Config,
    Dictionary<PatternType, TradeSimulator.PatternExitProfile> PatternExitProfiles,
    PatternParameterOverrides? ExitOverrides,
    BacktestPortfolioState Portfolio,
    IReadOnlyDictionary<string, RuleBasedDetector> DetectorsByName,
    IReadOnlyDictionary<string, BacktestStrategyRuntime> StrategyRuntimes,
    Dictionary<string, int> ReentryCooldowns,
    List<TradeRecord> Trades,
    TradeSimulator Simulator,
    Action<int> ApplyNewTradeCosts);

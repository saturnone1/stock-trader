using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>신호 다음 봉의 시가 재가격, 재사이징, 진입봉 체결 평가를 담당합니다.</summary>
internal sealed class BacktestPendingEntryProcessor
{
    private readonly Dictionary<string, BacktestPendingEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public bool Contains(string symbol) => _entries.ContainsKey(symbol);
    public bool TryAdd(string symbol, BacktestPendingEntry entry) => _entries.TryAdd(symbol, entry);
    public void Clear() => _entries.Clear();

    public void Process(BacktestPendingEntryContext context)
    {
        foreach (var symbol in _entries.Keys.ToList())
        {
            if (context.Portfolio.OpenPositions.ContainsKey(symbol))
            {
                _entries.Remove(symbol);
                continue;
            }
            if (!context.SymbolData.TryGetValue(symbol, out var data))
            {
                _entries.Remove(symbol);
                continue;
            }
            if (!data.TimestampToIndex.TryGetValue(context.Date, out var barIndex)) continue;

            var pending = _entries[symbol];
            var runtime = pending.StrategyName != null
                && context.StrategyRuntimes.TryGetValue(pending.StrategyName, out var resolvedRuntime)
                    ? resolvedRuntime
                    : null;
            if (IsBlocked(symbol, pending, runtime, barIndex, context))
            {
                _entries.Remove(symbol);
                continue;
            }

            var entryBar = data.Bars[barIndex];
            var fill = LongEntryFillPolicy.Reprice(
                pending.SignalEntryPrice,
                pending.SignalStopPrice,
                pending.SignalTargetPrice,
                entryBar.Open,
                fallbackTargetMultiple: 2m);
            if (fill == null)
            {
                _entries.Remove(symbol);
                continue;
            }

            var sizing = LongPositionSizingPolicy.CalculateWithCapFraction(
                pending.EquityAtSignal,
                pending.RiskFraction,
                fill.EntryPrice,
                fill.StopPrice,
                pending.PositionCapFraction);
            if (!sizing.CanEnter)
            {
                _entries.Remove(symbol);
                continue;
            }

            var position = BacktestOpenPositionFactory.CreateNextOpen(
                pending, fill, entryBar, barIndex, sizing.Quantity);

            // 시가 진입 봉의 고가·저가는 실제 보유 구간이므로 즉시 체결 정책을 평가한다.
            var tradesBefore = context.Trades.Count;
            var exitResult = context.ExecutionAdapter.ProcessExitLogic(
                position,
                entryBar,
                barIndex,
                data.Atr[barIndex],
                data.Sma200[barIndex],
                data.CumulativeRsi2[barIndex],
                data.CumulativeRsi2TrendMa[barIndex],
                context.CumulativeRsi2Config,
                context.PatternExitProfiles,
                context.ExitOverrides,
                symbol,
                context.Trades);
            context.ApplyNewTradeCosts(tradesBefore);

            if (exitResult != null)
            {
                context.Portfolio.OpenPositions[symbol] = exitResult;
            }
            else if (runtime != null && context.Trades.Count > tradesBefore)
            {
                BacktestStrategyTransitionPolicy.RegisterClosedTrade(
                    $"{pending.StrategyName}|{symbol}",
                    barIndex,
                    context.TimelineIndex,
                    context.Trades[^1],
                    runtime,
                    context.ReentryCooldowns);
            }

            _entries.Remove(symbol);
            if (runtime == null) continue;

            runtime.DailyEntryCount++;
            runtime.LastEntryDate = context.TradingDay;
        }
    }

    private static bool IsBlocked(
        string symbol,
        BacktestPendingEntry pending,
        BacktestStrategyRuntime? runtime,
        int barIndex,
        BacktestPendingEntryContext context)
    {
        var positionLimit = runtime?.Portfolio.MaxTotalPositions > 0
            ? Math.Min(context.MaxTotalPositions, runtime.Portfolio.MaxTotalPositions)
            : context.MaxTotalPositions;
        return context.Portfolio.OpenPositions.Count >= positionLimit
            || runtime?.CircuitBreakerTripped == true
            || runtime != null
                && runtime.CircuitBreaker.ConsecutiveLossLimit > 0
                && context.TimelineIndex < runtime.CircuitBreakerUntilStep
            || runtime != null
                && runtime.Portfolio.MaxEntriesPerDay > 0
                && runtime.DailyEntryCount >= runtime.Portfolio.MaxEntriesPerDay
            || pending.StrategyName != null
                && context.ReentryCooldowns.TryGetValue(
                    $"{pending.StrategyName}|{symbol}", out var cooldownUntil)
                && barIndex < cooldownUntil;
    }
}

internal sealed record BacktestPendingEntryContext(
    DateTime Date,
    DateOnly TradingDay,
    int TimelineIndex,
    int MaxTotalPositions,
    IReadOnlyDictionary<string, PreparedSymbolData> SymbolData,
    BacktestPortfolioState Portfolio,
    IReadOnlyDictionary<string, BacktestStrategyRuntime> StrategyRuntimes,
    Dictionary<string, int> ReentryCooldowns,
    List<TradeRecord> Trades,
    BacktestExecutionAdapter ExecutionAdapter,
    CumulativeRsi2Config CumulativeRsi2Config,
    Dictionary<PatternType, BacktestExecutionAdapter.PatternExitProfile> PatternExitProfiles,
    PatternParameterOverrides? ExitOverrides,
    Action<int> ApplyNewTradeCosts);

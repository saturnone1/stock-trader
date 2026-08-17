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
            var runtime = context.RuntimeRegistry.Find(pending.StrategyName);
            var cooldownUntil = context.RuntimeRegistry.GetCooldownUntil(
                pending.StrategyName, symbol);
            var eligibility = BacktestEntryEligibilityPolicy.Evaluate(
                new BacktestEntryEligibilityRequest(
                    context.MaxTotalPositions,
                    runtime?.Portfolio.MaxTotalPositions ?? 0,
                    context.Portfolio.OpenPositions.Count,
                    runtime?.CircuitBreakerTripped == true,
                    runtime?.CircuitBreaker.ConsecutiveLossLimit > 0,
                    context.TimelineIndex,
                    runtime?.CircuitBreakerUntilStep ?? 0,
                    runtime?.Portfolio.MaxEntriesPerDay ?? 0,
                    runtime?.DailyEntryCount ?? 0,
                    barIndex,
                    cooldownUntil));
            if (!eligibility.CanEnter)
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
            var tradesBefore = context.TradeLedger.Count;
            var exitResult = context.ExecutionAdapter.ProcessExitLogic(
                position,
                entryBar,
                barIndex,
                data.Atr[barIndex],
                data.TqqqProtectiveStopFloor[barIndex],
                data.CumulativeRsi2[barIndex],
                data.CumulativeRsi2TrendMa[barIndex],
                context.CumulativeRsi2Config,
                context.ExitPolicies,
                context.ExitOverrides,
                symbol,
                context.TradeLedger.Trades);
            context.TradeLedger.SettleSince(tradesBefore);

            if (exitResult != null)
            {
                context.Portfolio.OpenPositions[symbol] = exitResult;
            }
            else if (context.TradeLedger.Count > tradesBefore)
            {
                context.RuntimeRegistry.RegisterClosedTrade(
                    pending.StrategyName,
                    symbol,
                    barIndex,
                    context.TimelineIndex,
                    context.TradeLedger.Trades[^1]);
            }

            _entries.Remove(symbol);
            context.RuntimeRegistry.RegisterEntry(pending.StrategyName, context.TradingDay);
        }
    }

}

internal sealed record BacktestPendingEntryContext(
    DateTime Date,
    DateOnly TradingDay,
    int TimelineIndex,
    int MaxTotalPositions,
    IReadOnlyDictionary<string, PreparedSymbolData> SymbolData,
    BacktestPortfolioState Portfolio,
    BacktestStrategyRuntimeRegistry RuntimeRegistry,
    BacktestTradeLedger TradeLedger,
    BacktestExecutionAdapter ExecutionAdapter,
    CumulativeRsi2Config CumulativeRsi2Config,
    Dictionary<PatternType, LongPositionExitPolicy> ExitPolicies,
    PatternParameterOverrides? ExitOverrides);

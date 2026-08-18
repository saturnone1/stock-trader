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
            var instructions = BacktestStrategyExecutionInstructionResolver.Resolve(
                position,
                data,
                barIndex,
                context.MaxWindow,
                context.MaxTotalPositions,
                context.Portfolio.CurrentEquity,
                context.RuntimeRegistry);

            var tradesBefore = context.TradeLedger.Count;
            var exitResult = context.Simulator.ProcessExitLogic(
                position, data.Bars[barIndex], barIndex,
                data.Atr[barIndex], data.TqqqProtectiveStopFloor[barIndex],
                data.CumulativeRsi2[barIndex], data.CumulativeRsi2TrendMa[barIndex],
                context.CumulativeRsi2Config,
                context.ExitPolicies,
                context.ExitOverrides,
                symbol,
                context.TradeLedger.Trades,
                instructions.Exit,
                instructions.Scaling);
            context.TradeLedger.SettleSince(tradesBefore);

            if (exitResult == null)
            {
                ClosePositionState(symbol, position, barIndex, context, tradesBefore);
                continue;
            }

            position = exitResult;
            openPositions[symbol] = position;
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

using StockTrader.Application.Backtesting;

namespace StockTrader.Services.Backtest;

/// <summary>백테스트 종료 시 남은 포지션을 마지막 종가로 청산하고 보유 상태를 비웁니다.</summary>
internal static class BacktestTerminalPositionLiquidator
{
    public static void Liquidate(
        IReadOnlyDictionary<string, PreparedSymbolData> symbolData,
        BacktestPortfolioState portfolio,
        BacktestTradeLedger ledger)
    {
        var firstNewTrade = ledger.Count;
        foreach (var (symbol, position) in portfolio.OpenPositions.ToList())
        {
            if (!symbolData.TryGetValue(symbol, out var data) || data.Bars.Length == 0)
                continue;

            var lastBar = data.Bars[^1];
            var exitQuantity = position.CurrentQuantity > 0
                ? position.CurrentQuantity
                : position.Quantity;
            ledger.Trades.Add(BacktestExecutionAdapter.CreateTradeRecord(
                symbol,
                position,
                lastBar.Close,
                lastBar.Timestamp,
                "기간 종료",
                exitQuantity));
            portfolio.OpenPositions.Remove(symbol);
        }

        ledger.SettleSince(firstNewTrade);
        if (ledger.Count > 0)
            portfolio.RecordMarkedEquity(ledger.Trades.Max(trade => trade.ExitTime));
    }
}

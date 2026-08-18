using FluentAssertions;
using StockTrader.Domain.Backtesting;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestExecutionCostLedgerTests
{
    [Fact]
    public void ApplyNewTrades_AppliesFixedCostsOnlyOnce()
    {
        var ledger = new BacktestExecutionCostLedger(
            SlippageModel.Fixed, slippagePercent: 0.1m, commissionPerTrade: 1m);
        var trade = Trade(entryAtr: 0m, entryVolume: 0);
        var callbackCount = 0;

        ledger.ApplyNewTrades([trade], 0, _ => callbackCount++);
        ledger.ApplyNewTrades([trade], 0, _ => callbackCount++);

        trade.PnL.Should().Be(96.9m);
        trade.PnLPercent.Should().Be(0.0969m);
        ledger.TotalSlippage.Should().Be(2.1m);
        ledger.TotalCommission.Should().Be(1m);
        callbackCount.Should().Be(1);
    }

    [Fact]
    public void ApplyNewTrades_UsesAtrAndOrderSizeForAdaptiveSlippage()
    {
        var ledger = new BacktestExecutionCostLedger(
            SlippageModel.Adaptive, slippagePercent: 0.1m, commissionPerTrade: 0m);
        var trade = Trade(entryAtr: 2m, entryVolume: 1_000);

        ledger.ApplyNewTrades([trade], 0, _ => { });

        ledger.TotalSlippage.Should().Be(2.52m);
        trade.PnL.Should().Be(97.48m);
    }

    private static TradeRecord Trade(decimal entryAtr, long entryVolume) => new()
    {
        Symbol = "TEST",
        EntryPrice = 100m,
        ExitPrice = 110m,
        Quantity = 10,
        PnL = 100m,
        EntryAtr = entryAtr,
        EntryVolume = entryVolume
    };
}

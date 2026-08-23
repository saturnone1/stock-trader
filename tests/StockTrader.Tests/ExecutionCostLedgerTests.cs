using FluentAssertions;
using StockTrader.Domain.Backtesting;
using StockTrader.Engine.Execution;

namespace StockTrader.Tests;

public class ExecutionCostLedgerTests
{
    [Fact]
    public void TrySettle_AppliesFixedCostExactlyOncePerExecutionKey()
    {
        var ledger = new ExecutionCostLedger<string>(
            SlippageModel.Fixed, slippagePercent: 0.1m, commissionPerExecution: 1m);
        var input = Trade(entryAtr: 0m, entryVolume: 0);

        var first = ledger.TrySettle("trade-1", input);
        var duplicate = ledger.TrySettle("trade-1", input);
        var secondTrade = ledger.TrySettle("trade-2", input);

        first.Should().Be(new ExecutionCostResult(96.9m, 0.0969m, 2.1m, 1m));
        duplicate.Should().BeNull();
        secondTrade.Should().NotBeNull();
        ledger.TotalSlippage.Should().Be(4.2m);
        ledger.TotalCommission.Should().Be(2m);
    }

    [Fact]
    public void TrySettle_UsesVolatilityAndLiquidityForAdaptiveSlippage()
    {
        var ledger = new ExecutionCostLedger<int>(
            SlippageModel.Adaptive, slippagePercent: 0.1m, commissionPerExecution: 0m);

        var result = ledger.TrySettle(1, Trade(entryAtr: 2m, entryVolume: 1_000));

        result.Should().Be(new ExecutionCostResult(97.48m, 0.09748m, 2.52m, 0m));
    }

    private static ExecutionCostInput Trade(decimal entryAtr, long entryVolume) => new(
        EntryPrice: 100m,
        ExitPrice: 110m,
        Quantity: 10,
        GrossPnl: 100m,
        EntryAtr: entryAtr,
        EntryVolume: entryVolume);
}

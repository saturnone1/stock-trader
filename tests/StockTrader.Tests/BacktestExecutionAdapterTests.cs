using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestExecutionAdapterTests
{
    [Fact]
    public void ProcessExitLogic_UsesStopWhenStopAndTargetAreBothTouched()
    {
        var (simulator, position, trades) = Setup();
        var bar = Bar(open: 100m, high: 115m, low: 94m, close: 105m);

        var result = Process(simulator, position, bar, trades);

        result.Should().BeNull();
        trades.Should().ContainSingle();
        trades[0].ExitPrice.Should().Be(95m);
        trades[0].ExitReason.Should().Be("손절");
    }

    [Fact]
    public void ProcessExitLogic_FillsGapBelowStopAtOpen()
    {
        var (simulator, position, trades) = Setup();
        var bar = Bar(open: 90m, high: 96m, low: 88m, close: 94m);

        Process(simulator, position, bar, trades);

        trades[0].ExitPrice.Should().Be(90m);
    }

    [Fact]
    public void ProcessExitLogic_AppliesNewTrailingStopFromNextBar()
    {
        var (simulator, position, trades) = Setup(trailing: true);
        var activationBar = Bar(open: 101m, high: 120m, low: 105m, close: 115m);

        var stillOpen = Process(simulator, position, activationBar, trades);

        stillOpen.Should().NotBeNull();
        trades.Should().BeEmpty();
        stillOpen!.StopLoss.Should().Be(110m);

        var nextBar = Bar(open: 112m, high: 113m, low: 109m, close: 110m, day: 2);
        Process(simulator, stillOpen, nextBar, trades, barIndex: 2).Should().BeNull();
        trades[0].ExitPrice.Should().Be(110m);
    }

    [Fact]
    public void ProcessExitLogic_PartialExitPreservesEntryEquityAndUsesRemainingQuantity()
    {
        var (simulator, position, trades) = Setup(partial: true, equityAtEntry: 25_000m);
        var bar = Bar(open: 101m, high: 106m, low: 99m, close: 105m);

        var stillOpen = Process(simulator, position, bar, trades);

        stillOpen.Should().NotBeNull();
        stillOpen!.CurrentQuantity.Should().Be(5);
        stillOpen.Quantity.Should().Be(5);
        stillOpen.EquityAtEntry.Should().Be(25_000m);
        trades.Should().ContainSingle(trade => trade.ExitReason == "부분 익절(1R)" && trade.Quantity == 5);
    }

    [Fact]
    public void ProcessExitLogic_UsesSharedCumulativeRsi2TrendBreakDecision()
    {
        var (simulator, position, trades) = Setup(patternType: PatternType.CumulativeRsi2);
        var bar = Bar(open: 100m, high: 101m, low: 96m, close: 99m);

        var result = Process(
            simulator,
            position,
            bar,
            trades,
            cumulativeRsi2: 80m,
            cumulativeRsi2TrendMa: 100m,
            cumulativeConfig: new CumulativeRsi2Config
            {
                ExitThreshold = 70m,
                LongTrendMaPeriod = 200
            });

        result.Should().BeNull();
        trades.Should().ContainSingle(trade =>
            trade.ExitPrice == 99m && trade.ExitReason == "200SMA 이탈");
    }

    private static (BacktestExecutionAdapter simulator, BacktestExecutionAdapter.OpenPosition position, List<TradeRecord> trades) Setup(
        bool trailing = false,
        bool partial = false,
        decimal equityAtEntry = 0m,
        PatternType patternType = PatternType.Custom)
    {
        var profile = new LongPositionExitPolicy(
            MaxHoldingBars: 20,
            EnableTrailingStop: trailing,
            TrailingStopAtrMultiplier: 2m,
            TrailingActivationR: 1m,
            EnablePartialProfit: partial,
            PartialProfitRMultiple: partial ? 1m : 0m,
            EnableTargetExit: true,
            EnableTimeExit: false,
            BreakevenAtrMultiplier: 0m);
        var position = new BacktestExecutionAdapter.OpenPosition
        {
            PatternType = patternType,
            EntryPrice = 100m,
            OriginalStop = 95m,
            StopLoss = 95m,
            Target = trailing ? 200m : 110m,
            Quantity = 10,
            CurrentQuantity = 10,
            EntryTime = new DateTime(2024, 1, 1),
            EntryBarIndex = 0,
            EntryAtr = 5m,
            HighestHighSinceEntry = 100m,
            LowestLowSinceEntry = 100m,
            RiskDistance = 5m,
            EquityAtEntry = equityAtEntry,
            CustomExitProfile = profile
        };
        return (new BacktestExecutionAdapter(), position, []);
    }

    private static BacktestExecutionAdapter.OpenPosition? Process(
        BacktestExecutionAdapter simulator, BacktestExecutionAdapter.OpenPosition position, OhlcvBar bar,
        List<TradeRecord> trades,
        int barIndex = 1,
        decimal cumulativeRsi2 = 0m,
        decimal cumulativeRsi2TrendMa = 0m,
        CumulativeRsi2Config? cumulativeConfig = null) => simulator.ProcessExitLogic(
            position, bar, barIndex, 5m, 0m, cumulativeRsi2, cumulativeRsi2TrendMa,
            cumulativeConfig ?? new CumulativeRsi2Config(),
            new Dictionary<PatternType, LongPositionExitPolicy>(), null, "TQQQ", trades);

    private static OhlcvBar Bar(decimal open, decimal high, decimal low, decimal close, int day = 1) => new()
    {
        Timestamp = new DateTime(2024, 1, day),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 100_000
    };
}

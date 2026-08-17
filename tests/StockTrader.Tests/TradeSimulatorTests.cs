using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;

namespace StockTrader.Tests;

public class TradeSimulatorTests
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

    private static (TradeSimulator simulator, TradeSimulator.OpenPosition position, List<TradeRecord> trades) Setup(bool trailing = false)
    {
        var profile = new TradeSimulator.PatternExitProfile(
            MaxHoldingBars: 20,
            EnableTrailingStop: trailing,
            TrailingStopAtrMultiplier: 2m,
            TrailingActivationR: 1m,
            EnablePartialProfit: false,
            PartialProfitRMultiple: 0m,
            EnableTargetExit: true,
            EnableTimeExit: false,
            BreakevenAtrMultiplier: 0m);
        var position = new TradeSimulator.OpenPosition
        {
            PatternType = PatternType.Custom,
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
            CustomExitProfile = profile
        };
        return (new TradeSimulator(new IndicatorService(), NullLogger<TradeSimulator>.Instance), position, []);
    }

    private static TradeSimulator.OpenPosition? Process(
        TradeSimulator simulator, TradeSimulator.OpenPosition position, OhlcvBar bar,
        List<TradeRecord> trades, int barIndex = 1) => simulator.ProcessExitLogic(
            position, bar, barIndex, 5m, 0m, 0m, 0m, new CumulativeRsi2Config(),
            new Dictionary<PatternType, TradeSimulator.PatternExitProfile>(), null, "TQQQ", trades);

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

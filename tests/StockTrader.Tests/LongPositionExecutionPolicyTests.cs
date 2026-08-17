using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LongPositionExecutionPolicyTests
{
    private static readonly LongPositionExitPolicy Policy = new(
        MaxHoldingBars: 10,
        EnableTrailingStop: true,
        TrailingStopAtrMultiplier: 2m,
        TrailingActivationR: 1m,
        EnablePartialProfit: true,
        PartialProfitRMultiple: 1m,
        EnableTargetExit: true,
        EnableTimeExit: true);

    [Fact]
    public void Evaluate_StopWinsWhenSameBarAlsoTouchesPartialAndTarget()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 100, high: 120, low: 94, close: 115), 6, 2m, Policy);

        result.IsClosed.Should().BeTrue();
        result.Events.Should().ContainSingle();
        result.Events[0].Type.Should().Be(PositionExecutionEventType.Exit);
        result.Events[0].Price.Should().Be(95m);
        result.Events[0].Reason.Should().Be("손절");
    }

    [Fact]
    public void Evaluate_GapBelowStopFillsAtBarOpen()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 90, high: 96, low: 88, close: 94), 6, 2m, Policy);

        result.Events.Single().Price.Should().Be(90m);
    }

    [Fact]
    public void Evaluate_NewTrailingStopDoesNotApplyRetroactivelyToCurrentLow()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 101, high: 112, low: 96, close: 110), 6, 2m,
            Policy with { EnablePartialProfit = false, EnableTargetExit = false });

        result.IsClosed.Should().BeFalse();
        result.State.TrailingActivated.Should().BeTrue();
        result.State.StopPrice.Should().Be(108m);
        result.Events.Should().ContainSingle(item => item.Type == PositionExecutionEventType.StopMoved);
    }

    [Fact]
    public void Evaluate_PartialAndTargetCanBothFillAfterStopIsCleared()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 101, high: 116, low: 99, close: 114), 6, 2m, Policy);

        result.IsClosed.Should().BeTrue();
        result.Events.Select(item => item.Type).Should().Equal(
            PositionExecutionEventType.PartialExit,
            PositionExecutionEventType.Exit);
        result.Events[0].Quantity.Should().Be(50);
        result.Events[1].Quantity.Should().Be(50);
    }

    [Fact]
    public void Evaluate_ZeroMaximumHoldingBarsDisablesTimeExit()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 101, high: 104, low: 99, close: 102), 100, 2m,
            Policy with
            {
                MaxHoldingBars = 0,
                EnableTrailingStop = false,
                EnablePartialProfit = false,
                EnableTargetExit = false,
            });

        result.IsClosed.Should().BeFalse();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_StrategyExitWinsOverTimeExitAndSkipsNextBarStopUpdate()
    {
        var result = LongPositionExecutionPolicy.Evaluate(
            State(), Bar(open: 101, high: 112, low: 99, close: 110), 20, 2m,
            Policy with { EnablePartialProfit = false, EnableTargetExit = false },
            new StrategyExitInstruction(110m, "청산 규칙 충족"));

        result.IsClosed.Should().BeTrue();
        result.Events.Should().ContainSingle();
        result.Events[0].Reason.Should().Be("청산 규칙 충족");
    }

    [Fact]
    public void Reprice_PreservesSignalRiskAndRewardAtNextOpen()
    {
        var fill = LongEntryFillPolicy.Reprice(100m, 95m, 115m, 108m, 2m);

        fill.Should().Be(new LongEntryFill(108m, 103m, 123m, 5m));
    }

    [Theory]
    [InlineData(100, 100, 105)]
    [InlineData(100, 101, 105)]
    [InlineData(100, 90, 10)]
    public void Reprice_RejectsInvalidLongRiskGeometry(
        decimal signalEntry,
        decimal signalStop,
        decimal actualEntry)
    {
        LongEntryFillPolicy.Reprice(signalEntry, signalStop, 120m, actualEntry, 2m)
            .Should().BeNull();
    }

    private static LongPositionExecutionState State() => new(
        EntryPrice: 100m,
        StopPrice: 95m,
        TargetPrice: 115m,
        HighestPrice: 100m,
        LowestPrice: 100m,
        RiskDistance: 5m,
        EntryAtr: 2m,
        EntryBarIndex: 5,
        CurrentQuantity: 100);

    private static OhlcvBar Bar(decimal open, decimal high, decimal low, decimal close) => new()
    {
        Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 1_000_000,
    };
}

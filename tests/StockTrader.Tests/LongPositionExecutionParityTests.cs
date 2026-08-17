using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LongPositionExecutionParityTests
{
    private static readonly LongPositionExitPolicy Policy = new(
        MaxHoldingBars: 10,
        EnableTrailingStop: true,
        TrailingStopAtrMultiplier: 2m,
        TrailingActivationR: 1m,
        EnablePartialProfit: false,
        PartialProfitRMultiple: 0m,
        EnableTargetExit: true,
        EnableTimeExit: true);

    public static TheoryData<decimal, StrategyExitInstruction?, bool, string?> Snapshots() => new()
    {
        { 94m, null, false, "손절" },
        { 115m, new StrategyExitInstruction(115m, "전략 매도"), true, "목표 도달" },
        { 105m, new StrategyExitInstruction(105m, "전략 매도"), true, "전략 매도" },
        { 105m, null, true, "시간 청산(10봉)" },
        { 110m, null, false, null }
    };

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void BarAndLivePolicies_AgreeForEquivalentPriceSnapshots(
        decimal currentPrice,
        StrategyExitInstruction? strategyExit,
        bool timeExitReached,
        string? expectedReason)
    {
        var state = State();
        var barIndex = timeExitReached ? 10 : 5;
        var barResult = LongPositionExecutionPolicy.Evaluate(
            state,
            new OhlcvBar
            {
                Open = currentPrice,
                High = currentPrice,
                Low = currentPrice,
                Close = currentPrice
            },
            barIndex,
            2m,
            Policy,
            strategyExit);
        var liveResult = LiveLongPositionDecisionPolicy.Evaluate(
            state,
            currentPrice,
            2m,
            Policy,
            timeExitReached,
            strategyExit);

        liveResult.ShouldExit.Should().Be(barResult.IsClosed);
        if (expectedReason is not null)
        {
            barResult.Events.Last().Reason.Should().Be(expectedReason);
            liveResult.Reason.Should().Be(expectedReason);
        }
        else
        {
            barResult.IsClosed.Should().BeFalse();
            liveResult.ShouldExit.Should().BeFalse();
            liveResult.State.Should().Be(barResult.State);
        }
    }

    [Fact]
    public void BarAndLivePolicies_AdvanceAndTriggerTheSameTqqqTrendStop()
    {
        var noTargetPolicy = Policy with
        {
            EnableTrailingStop = false,
            EnableTargetExit = false,
            EnableTimeExit = false,
            BreakevenAtrMultiplier = 0m
        };
        var dynamicStopFloor = Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(100m, 0.99m);
        var state = State();
        var advanceBar = FlatBar(105m);

        var barAdvance = LongPositionExecutionPolicy.Evaluate(
            state, advanceBar, 1, 2m, noTargetPolicy, dynamicStopFloor: dynamicStopFloor);
        var liveAdvance = LiveLongPositionDecisionPolicy.Evaluate(
            state, 105m, 2m, noTargetPolicy, false, dynamicStopFloor: dynamicStopFloor);

        barAdvance.IsClosed.Should().BeFalse();
        liveAdvance.ShouldExit.Should().BeFalse();
        barAdvance.State.StopPrice.Should().Be(99m);
        liveAdvance.State.Should().Be(barAdvance.State);

        var barExit = LongPositionExecutionPolicy.Evaluate(
            barAdvance.State, FlatBar(98m), 2, 2m, noTargetPolicy,
            dynamicStopFloor: dynamicStopFloor);
        var liveExit = LiveLongPositionDecisionPolicy.Evaluate(
            liveAdvance.State, 98m, 2m, noTargetPolicy, false,
            dynamicStopFloor: dynamicStopFloor);

        barExit.IsClosed.Should().BeTrue();
        liveExit.ShouldExit.Should().BeTrue();
        liveExit.Reason.Should().Be(barExit.Events.Last().Reason);
    }

    private static OhlcvBar FlatBar(decimal price) => new()
    {
        Open = price,
        High = price,
        Low = price,
        Close = price
    };

    private static LongPositionExecutionState State() => new(
        EntryPrice: 100m,
        StopPrice: 95m,
        TargetPrice: 115m,
        HighestPrice: 100m,
        LowestPrice: 100m,
        RiskDistance: 5m,
        EntryAtr: 2m,
        EntryBarIndex: 0,
        CurrentQuantity: 10);
}

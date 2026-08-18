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
        var liveResult = LiveLongPositionExecutionAdapter.Evaluate(
            state,
            10,
            currentPrice,
            2m,
            Policy,
            timeExitReached,
            strategyExit);

        liveResult.ShouldExecute.Should().Be(barResult.IsClosed);
        if (expectedReason is not null)
        {
            barResult.Events.Last().Reason.Should().Be(expectedReason);
            liveResult.Reason.Should().Be(expectedReason);
        }
        else
        {
            barResult.IsClosed.Should().BeFalse();
            liveResult.ShouldExecute.Should().BeFalse();
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
        var liveAdvance = LiveLongPositionExecutionAdapter.Evaluate(
            state, 10, 105m, 2m, noTargetPolicy, false,
            dynamicStopFloor: dynamicStopFloor);

        barAdvance.IsClosed.Should().BeFalse();
        liveAdvance.ShouldExecute.Should().BeFalse();
        barAdvance.State.StopPrice.Should().Be(99m);
        liveAdvance.State.Should().Be(barAdvance.State);

        var barExit = LongPositionExecutionPolicy.Evaluate(
            barAdvance.State, FlatBar(98m), 2, 2m, noTargetPolicy,
            dynamicStopFloor: dynamicStopFloor);
        var liveExecution = LiveLongPositionExecutionAdapter.Evaluate(
            liveAdvance.State, 10, 98m, 2m, noTargetPolicy, false,
            dynamicStopFloor: dynamicStopFloor);

        barExit.IsClosed.Should().BeTrue();
        liveExecution.ShouldExecute.Should().BeTrue();
        liveExecution.Reason.Should().Be(barExit.Events.Last().Reason);
    }

    [Fact]
    public void CommonSessionAndLiveAdapter_AgreeOnPartialProfitIntent()
    {
        var partialPolicy = Policy with
        {
            EnablePartialProfit = true,
            PartialProfitRMultiple = 1m,
            EnableTargetExit = false,
            EnableTimeExit = false,
        };
        var state = State();
        var session = LongPositionExecutionSessionPolicy.Evaluate(
            new LongPositionSessionState(
                state,
                InitialQuantity: 10,
                TotalCost: 1_000m,
                RealizedPnl: 0m,
                new Dictionary<int, int>()),
            FlatBar(105m),
            barIndex: 1,
            currentAtr: 2m,
            partialPolicy);
        var live = LiveLongPositionExecutionAdapter.Evaluate(
            state,
            initialQuantity: 10,
            currentPrice: 105m,
            currentAtr: 2m,
            partialPolicy,
            timeExitReached: false);

        var partial = session.Events.Should().ContainSingle(item =>
            item.Type == LongPositionSessionEventType.PartialExit).Subject;
        live.Intent!.Quantity.Should().Be(partial.Quantity);
        live.Intent.Reason.Should().Be(partial.Reason);
        live.Intent.MarksPartialProfit.Should().BeTrue();
        live.State.CurrentQuantity.Should().Be(10,
            "live quantity changes only after the broker fill is reconciled");
    }

    [Theory]
    [InlineData("SCALE_IN", 5, StockTrader.Models.Enums.PositionExecutionKind.ScaleIn)]
    [InlineData("SCALE_OUT", 5, StockTrader.Models.Enums.PositionExecutionKind.ScaleOut)]
    public void CommonSessionAndLiveAdapter_AgreeOnScalingIntent(
        string direction,
        int expectedQuantity,
        StockTrader.Models.Enums.PositionExecutionKind expectedKind)
    {
        var holdPolicy = Policy with
        {
            EnableTrailingStop = false,
            EnableTargetExit = false,
            EnableTimeExit = false,
        };
        var state = State();
        var scaling = new LongPositionScalingInstruction(
            RuleIndex: 3,
            direction,
            Percent: 50m,
            MaxPositionCost: 2_000m);
        var session = LongPositionExecutionSessionPolicy.Evaluate(
            new LongPositionSessionState(
                state, 10, 1_000m, 0m, new Dictionary<int, int>()),
            FlatBar(100m),
            1,
            2m,
            holdPolicy,
            scaling: scaling);
        var live = LiveLongPositionExecutionAdapter.Evaluate(
            state,
            10,
            100m,
            2m,
            holdPolicy,
            false,
            scaling: scaling,
            scalingExecutionCounts: new Dictionary<int, int>());

        var scalingEvent = session.Events.Should().ContainSingle().Subject;
        scalingEvent.Quantity.Should().Be(expectedQuantity);
        live.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            expectedQuantity,
            scalingEvent.Reason,
            expectedKind,
            ScalingRuleIndex: 3));
        live.State.CurrentQuantity.Should().Be(10,
            "live quantity changes only after the broker fill is reconciled");
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

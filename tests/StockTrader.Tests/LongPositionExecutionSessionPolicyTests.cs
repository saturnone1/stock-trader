using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LongPositionExecutionSessionPolicyTests
{
    [Fact]
    public void Evaluate_AppliesPartialProfitAndScaleOutInOneOrderedSession()
    {
        var result = LongPositionExecutionSessionPolicy.Evaluate(
            State(quantity: 10),
            Bar(high: 106m, close: 105m),
            barIndex: 1,
            currentAtr: 5m,
            ExitPolicy(partial: true),
            scaling: new LongPositionScalingInstruction(
                2, StrategyCatalog.ScalingOutDirection, 20m));

        result.IsClosed.Should().BeFalse();
        result.Events.Select(item => item.Type).Should().Equal(
            LongPositionSessionEventType.PartialExit,
            LongPositionSessionEventType.ScaleOut);
        result.Events.Select(item => item.Quantity).Should().Equal(5, 2);
        result.State.Execution.CurrentQuantity.Should().Be(3);
        result.State.RealizedPnl.Should().Be(35m);
        result.State.TotalCost.Should().Be(300m);
        result.State.ScalingExecutionCounts.Should().ContainKey(2).WhoseValue.Should().Be(1);
    }

    [Fact]
    public void Evaluate_DoesNotScaleAfterAFullExit()
    {
        var result = LongPositionExecutionSessionPolicy.Evaluate(
            State(quantity: 10),
            Bar(high: 111m, close: 110m),
            barIndex: 1,
            currentAtr: 5m,
            ExitPolicy(partial: false),
            scaling: new LongPositionScalingInstruction(
                0, StrategyCatalog.ScalingInDirection, 20m));

        result.IsClosed.Should().BeTrue();
        result.Events.Should().ContainSingle(item => item.Type == LongPositionSessionEventType.Exit);
        result.Events.Should().NotContain(item => item.Type == LongPositionSessionEventType.ScaleIn);
        result.State.Execution.CurrentQuantity.Should().Be(0);
        result.State.RealizedPnl.Should().Be(100m);
        result.State.ScalingExecutionCounts.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_AppliesStrategyExitBeforeScalingAtTheClose()
    {
        var result = LongPositionExecutionSessionPolicy.Evaluate(
            State(quantity: 10),
            Bar(high: 101m, close: 99m),
            barIndex: 1,
            currentAtr: 5m,
            ExitPolicy(partial: false) with { EnableTargetExit = false },
            new StrategyExitInstruction(99m, "규칙 청산"),
            scaling: new LongPositionScalingInstruction(
                0, StrategyCatalog.ScalingOutDirection, 20m));

        result.IsClosed.Should().BeTrue();
        result.Events.Should().ContainSingle(item =>
            item.Type == LongPositionSessionEventType.Exit && item.Reason == "규칙 청산");
        result.State.RealizedPnl.Should().Be(-10m);
    }

    [Fact]
    public void Evaluate_CalculatesScaleInCapacityAfterSameBarPartialProfit()
    {
        var result = LongPositionExecutionSessionPolicy.Evaluate(
            State(quantity: 10),
            Bar(high: 106m, close: 105m),
            barIndex: 1,
            currentAtr: 5m,
            ExitPolicy(partial: true),
            scaling: new LongPositionScalingInstruction(
                0,
                StrategyCatalog.ScalingInDirection,
                20m,
                MaxPositionCost: 700m));

        result.Events.Select(item => item.Type).Should().Equal(
            LongPositionSessionEventType.PartialExit,
            LongPositionSessionEventType.ScaleIn);
        result.Events[^1].Quantity.Should().Be(1);
        result.State.Execution.CurrentQuantity.Should().Be(6);
        result.State.TotalCost.Should().Be(605m);
    }

    private static LongPositionSessionState State(int quantity) => new(
        new LongPositionExecutionState(
            100m, 95m, 110m, 100m, 100m, 5m, 5m, 0, quantity),
        quantity,
        100m * quantity,
        0m,
        new Dictionary<int, int>());

    private static LongPositionExitPolicy ExitPolicy(bool partial) => new(
        20, false, 0m, 0m, partial, partial ? 1m : 0m, true, false,
        BreakevenAtrMultiplier: 0m);

    private static OhlcvBar Bar(decimal high, decimal close) => new()
    {
        Open = 100m,
        High = high,
        Low = 99m,
        Close = close,
    };
}

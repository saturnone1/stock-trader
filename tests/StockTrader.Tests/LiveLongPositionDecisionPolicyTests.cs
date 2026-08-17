using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class LiveLongPositionDecisionPolicyTests
{
    private static readonly LongPositionExitPolicy Policy = new(
        10, true, 2m, 1m, false, 0m, true, true);

    [Fact]
    public void Evaluate_UsesExistingStopBeforeAnyUpdate()
    {
        var result = LiveLongPositionDecisionPolicy.Evaluate(State(), 94m, 2m, Policy, false);

        result.ShouldExit.Should().BeTrue();
        result.Reason.Should().Be("손절");
    }

    [Fact]
    public void Evaluate_TargetWinsOverStrategyAndTimeExit()
    {
        var result = LiveLongPositionDecisionPolicy.Evaluate(
            State(), 115m, 2m, Policy, true, new StrategyExitInstruction(115m, "전략 청산"));

        result.ShouldExit.Should().BeTrue();
        result.Reason.Should().Be("목표 도달");
    }

    [Fact]
    public void Evaluate_ZeroMaximumHoldingBarsDisablesTimeExit()
    {
        var result = LiveLongPositionDecisionPolicy.Evaluate(
            State(), 102m, 2m, Policy with { MaxHoldingBars = 0 }, true);

        result.ShouldExit.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_SharesProtectiveStopCalculationWithBarPolicy()
    {
        var live = LiveLongPositionDecisionPolicy.Evaluate(State(), 110m, 2m, Policy, false);

        live.ShouldExit.Should().BeFalse();
        live.State.TrailingActivated.Should().BeTrue();
        live.State.StopPrice.Should().Be(106m);
        live.StopUpdate.Should().NotBeNull();
    }

    private static LongPositionExecutionState State() => new(
        100m, 95m, 115m, 100m, 100m, 5m, 2m, 0, 10);
}

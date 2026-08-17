using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class LongPositionCloseDecisionPolicyTests
{
    private static readonly LongPositionExitPolicy Policy = new(
        10, true, 2m, 1m, false, 0m, true, true);

    [Fact]
    public void Resolve_TargetWinsOverStrategyAndTimeExit()
    {
        var result = LongPositionCloseDecisionPolicy.Resolve(
            115m,
            116m,
            110m,
            Policy,
            new StrategyExitInstruction(110m, "전략 매도"),
            timeExitReached: true);

        result.Should().Be(new StrategyExitInstruction(115m, "목표 도달"));
    }

    [Fact]
    public void Resolve_StrategyExitWinsOverTimeExit()
    {
        var result = LongPositionCloseDecisionPolicy.Resolve(
            115m,
            110m,
            110m,
            Policy,
            new StrategyExitInstruction(110m, "전략 매도"),
            timeExitReached: true);

        result.Should().Be(new StrategyExitInstruction(110m, "전략 매도"));
    }

    [Fact]
    public void Resolve_IgnoresInvalidTargetAndStrategyPrices()
    {
        var result = LongPositionCloseDecisionPolicy.Resolve(
            0m,
            110m,
            109m,
            Policy,
            new StrategyExitInstruction(0m, "잘못된 전략 매도"),
            timeExitReached: true);

        result.Should().Be(new StrategyExitInstruction(109m, "시간 청산(10봉)"));
    }

    [Fact]
    public void Resolve_DoesNotCreateZeroPriceTimeExit()
    {
        LongPositionCloseDecisionPolicy.Resolve(
                0m, 0m, 0m, Policy, null, timeExitReached: true)
            .Should().BeNull();
    }
}

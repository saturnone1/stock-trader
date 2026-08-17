using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class CumulativeRsi2ExitDecisionPolicyTests
{
    [Fact]
    public void Resolve_TrendBreakWinsOverRsiThreshold()
    {
        var decision = CumulativeRsi2ExitDecisionPolicy.Resolve(
            currentPrice: 99m,
            cumulativeRsi2: 80m,
            longTrendMovingAverage: 100m,
            exitThreshold: 70m,
            longTrendMovingAveragePeriod: 200);

        decision.Should().Be(new StrategyExitInstruction(99m, "200SMA 이탈"));
    }

    [Fact]
    public void Resolve_UsesCumulativeRsiThresholdWhenTrendIsIntact()
    {
        var decision = CumulativeRsi2ExitDecisionPolicy.Resolve(
            currentPrice: 101m,
            cumulativeRsi2: 70m,
            longTrendMovingAverage: 100m,
            exitThreshold: 70m,
            longTrendMovingAveragePeriod: 200);

        decision.Should().NotBeNull();
        decision!.Price.Should().Be(101m);
        decision.Reason.Should().StartWith("누적 RSI 청산(70");
    }

    [Fact]
    public void Resolve_HoldsWhenNeitherExitConditionIsMet()
    {
        CumulativeRsi2ExitDecisionPolicy.Resolve(101m, 69.9m, 100m, 70m, 200)
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_RejectsInvalidExecutionPrice()
    {
        CumulativeRsi2ExitDecisionPolicy.Resolve(0m, 90m, 100m, 70m, 200)
            .Should().BeNull();
    }
}

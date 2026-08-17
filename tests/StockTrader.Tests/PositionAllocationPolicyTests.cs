using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class PositionAllocationPolicyTests
{
    private static readonly WeightStrategy Strategy = new();

    [Fact]
    public void ResolveRegimeScale_UsesBearAndOverheatThresholds()
    {
        PositionAllocationPolicy.ResolveRegimeScale(
                Regime(price: 90m, movingAverage: 100m, above: false), Strategy)
            .Should().Be(0.3m);
        PositionAllocationPolicy.ResolveRegimeScale(
                Regime(price: 115m, movingAverage: 100m, above: true), Strategy)
            .Should().Be(0.7m);
        PositionAllocationPolicy.ResolveRegimeScale(
                Regime(price: 125m, movingAverage: 100m, above: true), Strategy)
            .Should().Be(0.4m);
        PositionAllocationPolicy.ResolveRegimeScale(
                Regime(price: 110m, movingAverage: 100m, above: true), Strategy)
            .Should().Be(1m);
    }

    [Fact]
    public void Apply_NormalizesInvalidScalesAndCountsIndependentReductions()
    {
        var reduced = PositionAllocationPolicy.Apply(100_000m, 0.5m, 0.8m);
        reduced.EffectiveEquity.Should().Be(40_000m);
        reduced.ReductionCount.Should().Be(2);

        PositionAllocationPolicy.Apply(100_000m, 1.2m, 0m)
            .EffectiveEquity.Should().Be(100_000m);
    }

    private static MarketRegime Regime(decimal price, decimal movingAverage, bool above) => new()
    {
        SpyPrice = price,
        Spy200Ma = movingAverage,
        SpyAbove200Ma = above
    };
}

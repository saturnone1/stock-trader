using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class PositionScaleInCapacityPolicyTests
{
    [Fact]
    public void CalculateMaxPositionCost_UsesTheStricterPortfolioOrStrategyCap()
    {
        var maxCost = PositionScaleInCapacityPolicy.CalculateMaxPositionCost(
            currentEquity: 100_000m,
            maxTotalPositions: 4,
            strategyMaxSinglePositionPercent: 20m);

        maxCost.Should().Be(20_000m);
    }

    [Fact]
    public void CalculateMaxPositionCost_FailsClosedForInvalidCapital()
    {
        PositionScaleInCapacityPolicy.CalculateMaxPositionCost(
                currentEquity: 0m,
                maxTotalPositions: 4,
                strategyMaxSinglePositionPercent: 0m)
            .Should().Be(0m);
    }
}

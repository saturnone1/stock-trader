using FluentAssertions;
using StockTrader.Application.Backtesting;

namespace StockTrader.Tests;

public class BacktestScaleInCapacityPolicyTests
{
    [Fact]
    public void CalculateMaxPositionCost_UsesTheStricterPortfolioOrStrategyCap()
    {
        var maxCost = BacktestScaleInCapacityPolicy.CalculateMaxPositionCost(
            currentEquity: 100_000m,
            maxTotalPositions: 4,
            strategyMaxSinglePositionPercent: 20m);

        maxCost.Should().Be(20_000m);
    }

    [Fact]
    public void CalculateMaxPositionCost_FailsClosedForInvalidCapital()
    {
        BacktestScaleInCapacityPolicy.CalculateMaxPositionCost(
                currentEquity: 0m,
                maxTotalPositions: 4,
                strategyMaxSinglePositionPercent: 0m)
            .Should().Be(0m);
    }
}

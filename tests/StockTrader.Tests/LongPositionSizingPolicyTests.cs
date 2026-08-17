using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Domain.Strategies;

namespace StockTrader.Tests;

public class LongPositionSizingPolicyTests
{
    [Fact]
    public void Calculate_UsesStopRiskAndPortfolioCapitalCap()
    {
        var decision = LongPositionSizingPolicy.Calculate(new LongPositionSizingRequest(
            AccountEquity: 10_000m,
            RiskFraction: 0.01m,
            EntryPrice: 100m,
            StopPrice: 95m,
            MaxTotalPositions: 10));

        decision.RiskCapital.Should().Be(2_000m);
        decision.PositionCapFraction.Should().Be(0.10m);
        decision.PositionCapital.Should().Be(1_000m);
        decision.Quantity.Should().Be(10);
    }

    [Fact]
    public void Calculate_HonorsStricterSinglePositionLimit()
    {
        var decision = LongPositionSizingPolicy.Calculate(new LongPositionSizingRequest(
            AccountEquity: 10_000m,
            RiskFraction: 0.01m,
            EntryPrice: 100m,
            StopPrice: 95m,
            MaxTotalPositions: 4,
            MaxSinglePositionPercent: 5m));

        decision.PositionCapFraction.Should().Be(0.05m);
        decision.Quantity.Should().Be(5);
    }

    [Fact]
    public void Calculate_RejectsEntryWhenCapitalCapCannotBuyOneShare()
    {
        var decision = LongPositionSizingPolicy.Calculate(new LongPositionSizingRequest(
            AccountEquity: 500m,
            RiskFraction: 0.01m,
            EntryPrice: 100m,
            StopPrice: 95m,
            MaxTotalPositions: 10));

        decision.CanEnter.Should().BeFalse();
        decision.Quantity.Should().Be(0);
    }

    [Fact]
    public void CalculateWithCapFraction_RepricesNextOpenWithoutBypassingCapitalCap()
    {
        var decision = LongPositionSizingPolicy.CalculateWithCapFraction(
            accountEquity: 500m,
            riskFraction: 0.01m,
            entryPrice: 100m,
            stopPrice: 95m,
            positionCapFraction: 0.10m);

        decision.CanEnter.Should().BeFalse();
    }

    [Fact]
    public void LiveCapitalHelpers_UseTheSamePortfolioCapAndFloorQuantity()
    {
        LongPositionSizingPolicy.ApplyPositionCapitalCap(
                desiredCapital: 20_000m,
                accountEquity: 100_000m,
                maxTotalPositions: 10)
            .Should().Be(10_000m);
        LongPositionSizingPolicy.ApplyPositionCapitalCap(
                desiredCapital: 20_000m,
                accountEquity: 100_000m,
                maxTotalPositions: 10,
                maxSinglePositionPercent: 5m)
            .Should().Be(5_000m);
        LongPositionSizingPolicy.CalculateAffordableQuantity(8_050m, 100m)
            .Should().Be(80);
    }

    [Fact]
    public void ResolveRiskFraction_UsesOnlyCompletedSamplesAfterMinimumCount()
    {
        var samples = Enumerable.Range(0, 10)
            .Select(index => index < 6
                ? new PositionSizingTradeSample(100m, 0.10m)
                : new PositionSizingTradeSample(-50m, -0.05m))
            .ToArray();

        LongPositionSizingPolicy.ResolveRiskFraction(0.01m, StrategyCatalog.KellySizingMode, samples)
            .Should().Be(0.25m);
        LongPositionSizingPolicy.ResolveRiskFraction(0.01m, StrategyCatalog.HalfKellySizingMode, samples)
            .Should().Be(0.125m);
        LongPositionSizingPolicy.ResolveRiskFraction(0.01m, StrategyCatalog.KellySizingMode, samples[..9])
            .Should().Be(0.01m);
    }
}

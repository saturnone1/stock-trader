using FluentAssertions;
using StockTrader.Domain.Trading;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class RiskRewardRatioPolicyTests
{
    [Theory]
    [InlineData(100, 10, 50, 0.05)]
    [InlineData(0, 10, 50, 0)]
    [InlineData(100, 0, 50, 0)]
    public void PositionReturnPolicyUsesEntryNotionalAndFailsClosed(
        decimal entry,
        int quantity,
        decimal pnl,
        decimal expected) =>
        PositionReturnPolicy.Calculate(entry, quantity, pnl).Should().Be(expected);

    [Fact]
    public void RecommendationAndDomainPolicyRetainTheSameAbsoluteStopSemantics()
    {
        var recommendation = new TradeRecommendation
        {
            EntryPrice = 100m,
            StopLossPrice = 103m,
            TargetPrice = 106m
        };

        recommendation.RiskRewardRatio.Should().Be(2m);
        recommendation.RiskRewardRatio.Should().Be(
            RiskRewardRatioPolicy.CalculateWithAbsoluteStopDistance(100m, 103m, 106m));
    }

    [Theory]
    [InlineData(100, 97, 106, 2)]
    [InlineData(100, 100, 106, 0)]
    [InlineData(100, 103, 106, 0)]
    public void LongPolicyFailsClosedForInvalidStopGeometry(
        decimal entry,
        decimal stop,
        decimal target,
        decimal expected) =>
        RiskRewardRatioPolicy.CalculateLong(entry, stop, target).Should().Be(expected);
}

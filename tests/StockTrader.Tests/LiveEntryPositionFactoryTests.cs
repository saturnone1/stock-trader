using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LiveEntryPositionFactoryTests
{
    private static readonly DateTime OpenedAt = new(
        2026, 8, 18, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_UsesBrokerFillAndReanchorsRiskGeometry()
    {
        var position = LiveEntryPositionFactory.Create(
            Recommendation(),
            new Position
            {
                Symbol = "TQQQ",
                Quantity = 7,
                EntryPrice = 108m,
                CurrentPrice = 109m,
            },
            accountId: 42,
            OpenedAt);

        position.AccountId.Should().Be(42);
        position.Quantity.Should().Be(7);
        position.EntryPrice.Should().Be(108m);
        position.CurrentPrice.Should().Be(109m);
        position.StopLossPrice.Should().Be(103m);
        position.TargetPrice.Should().Be(118m);
        position.InitialRiskDistance.Should().Be(5m);
        position.HighSinceEntry.Should().Be(108m);
        position.OpenedAt.Should().Be(OpenedAt);
    }

    [Fact]
    public void Create_FallsBackToRecommendationWhenBrokerFillIsUnavailable()
    {
        var recommendation = Recommendation();

        var position = LiveEntryPositionFactory.Create(
            recommendation, brokerPosition: null, accountId: 0, OpenedAt);

        position.Quantity.Should().Be(recommendation.ShareQuantity);
        position.EntryPrice.Should().Be(recommendation.EntryPrice);
        position.CurrentPrice.Should().Be(recommendation.EntryPrice);
        position.StopLossPrice.Should().Be(recommendation.StopLossPrice);
        position.TargetPrice.Should().Be(recommendation.TargetPrice);
        position.InitialRiskDistance.Should().Be(5m);
    }

    [Fact]
    public void Create_PreservesStrategyIdentityAcrossOrderEntryPaths()
    {
        var recommendation = Recommendation();
        recommendation.PatternType = PatternType.Custom;
        recommendation.CustomPatternName = "공통 체결 전략";

        var position = LiveEntryPositionFactory.Create(
            recommendation, brokerPosition: null, accountId: 3, OpenedAt);

        position.PatternType.Should().Be(PatternType.Custom);
        position.CustomPatternName.Should().Be("공통 체결 전략");
    }

    private static TradeRecommendation Recommendation() => new()
    {
        Symbol = "TQQQ",
        PatternType = PatternType.GapUpPullback,
        ShareQuantity = 10,
        EntryPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
    };
}

using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LiveEntryPositionFactoryTests
{
    private static readonly DateTime OpenedAt = new(
        2026, 8, 18, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateFromFill_UsesBrokerEvidenceAndReanchorsRiskGeometry()
    {
        var position = LiveEntryPositionFactory.CreateFromFill(
            Recommendation(),
            accountId: 42,
            filledQuantity: 7,
            averageFillPrice: 108m,
            OpenedAt);

        position.AccountId.Should().Be(42);
        position.SourceSignalId.Should().Be(88);
        position.Quantity.Should().Be(7);
        position.EntryPrice.Should().Be(108m);
        position.CurrentPrice.Should().Be(108m);
        position.StopLossPrice.Should().Be(103m);
        position.TargetPrice.Should().Be(118m);
        position.InitialRiskDistance.Should().Be(5m);
        position.HighSinceEntry.Should().Be(108m);
        position.OpenedAt.Should().Be(OpenedAt);
    }

    [Fact]
    public void CreateFromFill_PreservesStrategyIdentityAcrossOrderEntryPaths()
    {
        var recommendation = Recommendation();
        recommendation.PatternType = PatternType.Custom;
        recommendation.CustomPatternName = "공통 체결 전략";

        var position = LiveEntryPositionFactory.CreateFromFill(
            recommendation,
            accountId: 3,
            filledQuantity: 10,
            averageFillPrice: 100m,
            OpenedAt);

        position.PatternType.Should().Be(PatternType.Custom);
        position.CustomPatternName.Should().Be("공통 체결 전략");
    }

    private static TradeRecommendation Recommendation() => new()
    {
        SourceSignalId = 88,
        Symbol = "TQQQ",
        PatternType = PatternType.GapUpPullback,
        ShareQuantity = 10,
        EntryPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
    };
}

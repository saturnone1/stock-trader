using FluentAssertions;
using Moq;
using StockTrader.Application.Trading;

namespace StockTrader.Tests;

public sealed class TradeActivityQueryTests
{
    [Fact]
    public async Task RecommendationsUseOneObservationTimeAndProjectExecutionEvidence()
    {
        var observedAt = AtHour(12);
        var clock = new CountingTimeProvider(observedAt);
        var store = new Mock<ITradeActivityStore>();
        store.Setup(value => value.GetRecommendationsAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TradeRecommendationActivity(
                    7, 3, "TQQQ", PatternType.Breakout, null,
                    100m, 95m, 110m, 1_000m, 10, 0.2m, false,
                    OrderMode.AlertOnly, AtHour(10), AtHour(11), 4, true, "waiting")
            ]);
        var sut = new TradeActivityQueryService(store.Object, clock);

        var outcome = await sut.GetRecommendationsAsync(null);

        outcome.Succeeded.Should().BeTrue();
        clock.GetUtcNowCalls.Should().Be(1);
        outcome.Value!.Count.Should().Be(1);
        var row = outcome.Value.Recommendations.Single();
        row.EntryStatus.Should().Be("AwaitingBroker");
        row.PendingSeconds.Should().Be(3600);
        row.HasBrokerOrderId.Should().BeTrue();
        row.RiskRewardRatio.Should().Be(2m);
        row.StopLossPercent.Should().Be(0.05m);
        row.Pattern.Should().Be(nameof(PatternType.Breakout));
        row.PatternName.Should().Be("가격 돌파");
        row.ModeName.Should().Be("알림만 받기");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task InvalidRecommendationCountFailsBeforePersistence(int count)
    {
        var store = new Mock<ITradeActivityStore>();
        var sut = new TradeActivityQueryService(store.Object, TimeProvider.System);

        var outcome = await sut.GetRecommendationsAsync(count);

        outcome.Succeeded.Should().BeFalse();
        outcome.Errors.Should().ContainSingle();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CustomRecommendationUsesItsStoredInvestorFacingName()
    {
        var store = new Mock<ITradeActivityStore>();
        store.Setup(value => value.GetRecommendationsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TradeRecommendationActivity(
                    8, null, "AAPL", PatternType.Custom, "내 반등 전략",
                    100m, 95m, 110m, 1_000m, 10, 0.2m, false,
                    OrderMode.AutoOrder, AtHour(10), null, null, false, null)
            ]);
        var sut = new TradeActivityQueryService(store.Object, TimeProvider.System);

        var outcome = await sut.GetRecommendationsAsync(1);

        outcome.Value!.Recommendations.Single().PatternName.Should().Be("내 반등 전략");
        outcome.Value.Recommendations.Single().Pattern.Should().Be(nameof(PatternType.Custom));
        outcome.Value.Recommendations.Single().ModeName.Should().Be("자동 주문");
    }

    [Fact]
    public async Task HistoryAppliesCentralDefaultsAndProjectsHoldingDays()
    {
        var store = new Mock<ITradeActivityStore>();
        store.Setup(value => value.GetHistoryAsync(
                PatternType.Breakout, OnDay(1), OnDay(20), 0, 50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeHistorySlice(2, [
                new CompletedTradeActivity(
                    1, "AAPL", PatternType.Breakout, null, 100m, 110m, 2,
                    20m, 0.1m, "target", OnDay(2), OnDay(5)),
                new CompletedTradeActivity(
                    2, "MSFT", PatternType.Breakout, null, 200m, 190m, 1,
                    -10m, -0.05m, "stop", OnDay(9), OnDay(8))
            ]));
        var sut = new TradeActivityQueryService(store.Object, TimeProvider.System);

        var outcome = await sut.GetHistoryAsync(new(
            PatternType.Breakout, OnDay(1), OnDay(20), null, null));

        outcome.Succeeded.Should().BeTrue();
        outcome.Value!.TotalCount.Should().Be(2);
        outcome.Value.Skip.Should().Be(0);
        outcome.Value.Take.Should().Be(50);
        outcome.Value.Trades.Select(row => row.HoldingDays).Should().Equal(3, 0);
        outcome.Value.Trades.Select(row => row.IsWin).Should().Equal(true, false);
    }

    [Fact]
    public async Task InvalidHistoryRangeAndPaginationFailBeforePersistence()
    {
        var store = new Mock<ITradeActivityStore>();
        var sut = new TradeActivityQueryService(store.Object, TimeProvider.System);

        var outcome = await sut.GetHistoryAsync(new(
            (PatternType)999, OnDay(10), OnDay(1), -1, 501));

        outcome.Succeeded.Should().BeFalse();
        outcome.Errors.Should().HaveCount(4);
        store.VerifyNoOtherCalls();
    }

    private static DateTime AtHour(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);

    private static DateTime OnDay(int day) =>
        new(2026, 8, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class CountingTimeProvider(DateTime observedAt) : TimeProvider
    {
        public int GetUtcNowCalls { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCalls++;
            return new DateTimeOffset(observedAt);
        }
    }
}

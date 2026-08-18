using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Trading;

namespace StockTrader.Tests;

public sealed class TradeActivityContractTests
{
    [Fact]
    public void RecommendationContractPreservesTheApplicationProjection()
    {
        var generatedAt = Utc(10);
        var requestedAt = Utc(11);
        var source = new TradeRecommendationView(
            1, 2, "TQQQ", "Breakout", "가격 돌파", 100m, 95m, 110m, 1_000m, 10,
            0.2m, 2m, 0.05m, false, "AwaitingBroker", 4, true, 60,
            "waiting", "AlertOnly", "알림만 받기", generatedAt, requestedAt);

        var response = TradeRecommendationResponse.Create(source);

        response.Should().BeEquivalentTo(source);
    }

    [Fact]
    public void HistoryContractPreservesPagingAndTradeIdentity()
    {
        var source = new TradeHistoryPage(7, 2, 50, [
            new TradeHistoryView(
                3, "AAPL", "Breakout", "가격 돌파", 100m, 105m, 2, 10m, 0.05m,
                true, "target", Utc(1), Utc(4), 3)
        ]);

        var response = TradeHistoryResponse.Create(source);

        response.TotalCount.Should().Be(7);
        response.Skip.Should().Be(2);
        response.Take.Should().Be(50);
        response.Trades.Should().ContainSingle().Which
            .Should().BeEquivalentTo(source.Trades.Single());
    }

    private static DateTime Utc(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);
}

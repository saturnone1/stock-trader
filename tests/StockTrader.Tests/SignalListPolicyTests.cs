using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Signals;
using StockTrader.Application.Statistics;
using StockTrader.Domain.Strategies;

namespace StockTrader.Tests;

public class SignalListPolicyTests
{
    private static readonly DateTime Now =
        new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_MultipleSymbolStatisticsPreferExactThenAggregateWithoutDuplicateKeyFailure()
    {
        var signals = new[]
        {
            Signal(1, "AAPL", Now),
            Signal(2, "GOOG", Now.AddMinutes(-1))
        };
        var statistics = new[]
        {
            Statistic("MSFT", 0.70m, Now.AddDays(-1)),
            Statistic(null, 0.50m, Now.AddDays(-2)),
            Statistic("AAPL", 0.80m, Now)
        };

        var result = SignalListPolicy.Evaluate(
            signals,
            statistics,
            new SignalBrowseRequest(null, null, "latest", null));

        result.Count.Should().Be(2);
        result.Signals.Single(signal => signal.Symbol == "AAPL")
            .PatternWinRate.Should().Be(0.80m);
        result.Signals.Single(signal => signal.Symbol == "GOOG")
            .PatternWinRate.Should().Be(0.50m);
    }

    [Fact]
    public void Evaluate_FiltersCaseInsensitivelyAndUsesDeterministicRiskRewardOrdering()
    {
        var signals = new[]
        {
            Signal(1, "AAPL", Now, target: 110m),
            Signal(3, "AAL", Now, target: 115m),
            Signal(2, "MSFT", Now.AddMinutes(1), target: 120m)
        };

        var result = SignalListPolicy.Evaluate(
            signals,
            [],
            new SignalBrowseRequest("breakout", "aa", "rr", "Long"));

        result.Signals.Select(signal => signal.Id).Should().Equal(3, 1);
        result.Signals.Select(signal => signal.RiskReward).Should().Equal(3m, 2m);
    }

    [Fact]
    public void Evaluate_InvalidRiskGeometryReturnsZeroAndConfidenceTiesUseObservationOrder()
    {
        var result = SignalListPolicy.Evaluate(
            [
                Signal(1, "AAPL", Now, stop: 100m),
                Signal(2, "MSFT", Now.AddMinutes(1), stop: 101m)
            ],
            [],
            new SignalBrowseRequest(null, null, "confidence", null));

        result.Signals.Select(signal => signal.Id).Should().Equal(2, 1);
        result.Signals.Should().OnlyContain(signal => signal.RiskReward == 0m);
    }

    [Fact]
    public void ResponseMapperPreservesSignalWireContract()
    {
        var snapshot = SignalListPolicy.Evaluate(
            [Signal(7, "TQQQ", Now)],
            [Statistic("TQQQ", 0.75m, Now)],
            new SignalBrowseRequest(null, null, null, null));

        var response = SignalListResponse.Create(snapshot);

        response.Count.Should().Be(1);
        response.Signals[0].Pattern.Should().Be("Breakout");
        response.Signals[0].DetectedAt.Should().Be(Now.ToString("o"));
        response.Signals[0].PatternWinRate.Should().Be(0.75m);
    }

    private static BrowsableSignal Signal(
        long id,
        string symbol,
        DateTime detectedAt,
        decimal stop = 95m,
        decimal target = 110m) => new(
        id,
        symbol,
        PatternType.Breakout,
        100m,
        stop,
        target,
        0.60m,
        "test",
        detectedAt);

    private static PatternStatisticsSnapshot Statistic(
        string? symbol,
        decimal winRate,
        DateTime updatedAt) => new(
        PatternType.Breakout,
        symbol,
        10,
        winRate,
        0.10m,
        0.05m,
        0.08m,
        updatedAt);
}

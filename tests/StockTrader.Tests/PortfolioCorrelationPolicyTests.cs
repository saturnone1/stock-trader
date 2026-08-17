using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Models;

namespace StockTrader.Tests;

public class PortfolioCorrelationPolicyTests
{
    [Fact]
    public void ComputePearsonCorrelation_UsesAlignedHistoricalReturns()
    {
        var returns = Enumerable.Range(0, 70)
            .Select(index => (index % 7 - 3) / 1_000m)
            .ToArray();
        var first = Prepared(returns);
        var sameDirection = Prepared(returns.Select(value => value * 2m).ToArray());
        var oppositeDirection = Prepared(returns.Select(value => -value).ToArray());
        var asOf = first.Bars[^1].Timestamp;

        PortfolioCorrelationPolicy.ComputePearsonCorrelation(first, sameDirection, asOf)
            .Should().BeApproximately(1d, 0.0000001d);
        PortfolioCorrelationPolicy.ComputePearsonCorrelation(first, oppositeDirection, asOf)
            .Should().BeApproximately(-1d, 0.0000001d);
    }

    [Fact]
    public void ExceedsLimit_BlocksOnlyCorrelationAboveConfiguredMaximum()
    {
        var returns = Enumerable.Range(0, 70)
            .Select(index => (index % 5 - 2) / 500m)
            .ToArray();
        var data = new Dictionary<string, PreparedSymbolData>
        {
            ["OPEN"] = Prepared(returns),
            ["SAME"] = Prepared(returns),
            ["OPPOSITE"] = Prepared(returns.Select(value => -value).ToArray())
        };
        var asOf = data["OPEN"].Bars[^1].Timestamp;

        PortfolioCorrelationPolicy.ExceedsLimit(
            "SAME", ["OPEN"], data, asOf, 0.8m).Should().BeTrue();
        PortfolioCorrelationPolicy.ExceedsLimit(
            "OPPOSITE", ["OPEN"], data, asOf, 0.8m).Should().BeFalse();
    }

    [Fact]
    public void ComputePearsonCorrelation_ReturnsZeroWhenSamplesAreInsufficient()
    {
        var shortSeries = Prepared([0.01m, -0.01m, 0.02m]);

        PortfolioCorrelationPolicy.ComputePearsonCorrelation(
                shortSeries, shortSeries, shortSeries.Bars[^1].Timestamp)
            .Should().Be(0d);
    }

    private static PreparedSymbolData Prepared(IReadOnlyList<decimal> returns)
    {
        var closes = new decimal[returns.Count + 1];
        closes[0] = 100m;
        for (var index = 0; index < returns.Count; index++)
            closes[index + 1] = closes[index] * (1m + returns[index]);

        var bars = closes.Select((close, index) => new OhlcvBar
        {
            Symbol = "TEST",
            Timestamp = new DateTime(2024, 1, 1).AddDays(index),
            Close = close
        }).ToArray();
        return new PreparedSymbolData(
            bars,
            new decimal[bars.Length],
            closes,
            new decimal[bars.Length],
            new decimal[bars.Length],
            new decimal[bars.Length],
            bars.Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(pair => pair.Timestamp, pair => pair.index));
    }
}

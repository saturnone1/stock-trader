using FluentAssertions;
using StockTrader.Application.MarketData;

namespace StockTrader.Tests;

public sealed class MarketDataRequestWindowPolicyTests
{
    [Fact]
    public void Resolve_PreservesUtcInstantsExactly()
    {
        var from = new DateTime(2026, 7, 15, 13, 30, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 15, 20, 0, 0, DateTimeKind.Utc);

        var result = MarketDataRequestWindowPolicy.Resolve(from, to);

        result.StartUtc.Should().Be(from);
        result.EndUtc.Should().Be(to);
        result.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        result.EndUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Resolve_TreatsUnspecifiedApiDatesAsUtcWithoutHostTimeZoneConversion()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var to = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var result = MarketDataRequestWindowPolicy.Resolve(from, to);

        result.StartUtc.Should().Be(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        result.EndUtc.Should().Be(
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_RejectsAnEmptyOrReversedWindow()
    {
        var boundary = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var action = () => MarketDataRequestWindowPolicy.Resolve(boundary, boundary);

        action.Should().Throw<ArgumentException>();
    }
}

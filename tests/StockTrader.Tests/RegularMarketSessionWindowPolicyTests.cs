using FluentAssertions;
using StockTrader.Application.MarketData;
using TimeZoneConverter;

namespace StockTrader.Tests;

public sealed class RegularMarketSessionWindowPolicyTests
{
    private static readonly TimeZoneInfo Eastern =
        TZConvert.GetTimeZoneInfo("America/New_York");

    [Theory]
    [InlineData(2026, 1, 15, 14, 30, 21, 0)]
    [InlineData(2026, 7, 15, 13, 30, 20, 0)]
    public void Resolve_UsesTheMarketTimeZoneAcrossStandardAndDaylightTime(
        int year,
        int month,
        int day,
        int expectedStartHourUtc,
        int expectedStartMinuteUtc,
        int expectedEndHourUtc,
        int expectedEndMinuteUtc)
    {
        var result = RegularMarketSessionWindowPolicy.Resolve(
            new DateOnly(year, month, day),
            Eastern,
            new TimeSpan(9, 30, 0),
            new TimeSpan(16, 0, 0));

        result.StartUtc.Should().Be(
            new DateTime(year, month, day, expectedStartHourUtc, expectedStartMinuteUtc, 0, DateTimeKind.Utc));
        result.EndUtc.Should().Be(
            new DateTime(year, month, day, expectedEndHourUtc, expectedEndMinuteUtc, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_RejectsAReversedSession()
    {
        var action = () => RegularMarketSessionWindowPolicy.Resolve(
            new DateOnly(2026, 1, 15),
            Eastern,
            new TimeSpan(16, 0, 0),
            new TimeSpan(9, 30, 0));

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}

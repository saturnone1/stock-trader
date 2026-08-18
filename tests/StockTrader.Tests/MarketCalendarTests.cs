using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Market;

namespace StockTrader.Tests;

public class MarketCalendarTests
{
    [Fact]
    public void IsMarketOpen_UsesInjectedClockAndEasternTime()
    {
        var calendar = new MarketCalendar(new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero)));

        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeTrue();
        calendar.GetLocalNow(MarketRegion.UnitedStates).Hour.Should().Be(10);
    }

    [Fact]
    public void IsMarketOpen_RejectsWeekendEvenDuringSessionHours()
    {
        var calendar = new MarketCalendar(new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 22, 14, 0, 0, TimeSpan.Zero)));

        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeFalse();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

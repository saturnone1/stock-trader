using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Market;

namespace StockTrader.Tests;

public class MarketCalendarTests
{
    [Fact]
    public void IsMarketOpen_UsesInjectedClockAndEasternTime()
    {
        var calendar = Create(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));

        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeTrue();
        calendar.GetLocalNow(MarketRegion.UnitedStates).Hour.Should().Be(10);
    }

    [Fact]
    public void IsMarketOpen_RejectsWeekendEvenDuringSessionHours()
    {
        var calendar = Create(new DateTimeOffset(2026, 8, 22, 14, 0, 0, TimeSpan.Zero));

        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeFalse();
    }

    [Fact]
    public void IsMarketOpen_RejectsAnExchangeHolidayThatFallsOnAWeekday()
    {
        // 2026-11-26 은 목요일 추수감사절. 주말 전용 규칙은 이 날을 정규장으로 잘못 판정했다.
        var calendar = Create(new DateTimeOffset(2026, 11, 26, 15, 0, 0, TimeSpan.Zero));

        calendar.GetLocalNow(MarketRegion.UnitedStates).DayOfWeek
            .Should().Be(DayOfWeek.Thursday);
        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeFalse();
    }

    [Fact]
    public void IsMarketOpen_RejectsTheAfternoonOfAnEarlyCloseDay()
    {
        // 2026-11-27 은 추수감사절 다음날로 13:00 ET 조기마감.
        // 14:00 ET 는 정규 마감(16:00) 이전이지만 실제로는 이미 마감된 시각이다.
        var calendar = Create(new DateTimeOffset(2026, 11, 27, 19, 0, 0, TimeSpan.Zero));

        calendar.GetLocalNow(MarketRegion.UnitedStates).Hour.Should().Be(14);
        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeFalse();
    }

    [Fact]
    public void IsMarketOpen_AcceptsTheMorningOfAnEarlyCloseDay()
    {
        // 같은 조기마감일의 11:00 ET 는 정상적으로 열려 있다.
        var calendar = Create(new DateTimeOffset(2026, 11, 27, 16, 0, 0, TimeSpan.Zero));

        calendar.GetLocalNow(MarketRegion.UnitedStates).Hour.Should().Be(11);
        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeTrue();
    }

    [Fact]
    public void IsMarketOpen_RejectsAKoreanExchangeHoliday()
    {
        // 2026-08-17 은 광복절 대체공휴일(월요일).
        var calendar = Create(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero));

        calendar.GetLocalNow(MarketRegion.Korea).Hour.Should().Be(11);
        calendar.IsMarketOpen(MarketRegion.Korea).Should().BeFalse();
    }

    [Fact]
    public void IsMarketOpen_FailsClosedWhenTheCalendarHasNoEvidenceForTheDate()
    {
        // 캘린더 보유 범위를 벗어난 시각. 휴장일일 수도 있으므로 열림으로 추측하지 않는다.
        var calendar = Create(new DateTimeOffset(2030, 6, 12, 14, 0, 0, TimeSpan.Zero));

        calendar.IsMarketOpen(MarketRegion.UnitedStates).Should().BeFalse();
    }

    [Fact]
    public void GetTradingDay_ThrowsForDatesOutsideCalendarCoverage()
    {
        var calendar = Create(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));

        var act = () => calendar.GetTradingDay(MarketRegion.UnitedStates, new DateOnly(2030, 6, 12));

        act.Should().Throw<MarketCalendarCoverageException>();
    }

    [Fact]
    public void GetTradingDay_ReportsTheEarlyCloseTimeAsEvidence()
    {
        var calendar = Create(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));

        var evidence = calendar.GetTradingDay(
            MarketRegion.UnitedStates, new DateOnly(2026, 11, 27));

        evidence.Status.Should().Be(TradingDayStatus.EarlyClose);
        evidence.EarlyCloseTime.Should().Be(new TimeSpan(13, 0, 0));
        evidence.IsTradingDay.Should().BeTrue();
    }

    private static MarketCalendar Create(DateTimeOffset utcNow) =>
        new(new FixedTimeProvider(utcNow), NullLogger<MarketCalendar>.Instance);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

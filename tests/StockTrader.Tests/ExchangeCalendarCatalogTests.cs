using FluentAssertions;
using StockTrader.Domain.MarketData;

namespace StockTrader.Tests;

/// <summary>
/// 주말 전용 규칙이 거래소 휴장·조기마감 근거로 교체된 결과를 고정한다.
/// 아래 각 케이스는 이전 구현이 잘못 답하던 날짜들이다.
/// </summary>
public sealed class ExchangeCalendarCatalogTests
{
    [Theory]
    // 미국: 주중에 오는 대표 휴장일
    [InlineData(2026, 1, 1)]    // 신정 (목)
    [InlineData(2026, 7, 3)]    // 독립기념일 관측 (금)
    [InlineData(2026, 11, 26)]  // 추수감사절 (목)
    [InlineData(2026, 12, 25)]  // 성탄절 (금)
    public void UnitedStatesWeekdayHolidaysAreNotTradingDays(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        date.DayOfWeek.Should().NotBe(DayOfWeek.Saturday);
        date.DayOfWeek.Should().NotBe(DayOfWeek.Sunday);

        var evidence = ExchangeCalendarCatalog.GetTradingDay(MarketRegion.UnitedStates, date);

        evidence.Status.Should().Be(TradingDayStatus.Holiday);
        evidence.IsTradingDay.Should().BeFalse();
    }

    [Theory]
    // 한국: 주중에 오는 대표 휴장일
    [InlineData(2026, 3, 2)]    // 삼일절 대체 (월)
    [InlineData(2026, 5, 25)]   // 부처님오신날 (월)
    [InlineData(2026, 8, 17)]   // 광복절 대체 (월)
    [InlineData(2026, 12, 25)]  // 성탄절 (금)
    public void KoreaWeekdayHolidaysAreNotTradingDays(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        date.DayOfWeek.Should().NotBe(DayOfWeek.Saturday);
        date.DayOfWeek.Should().NotBe(DayOfWeek.Sunday);

        var evidence = ExchangeCalendarCatalog.GetTradingDay(MarketRegion.Korea, date);

        evidence.Status.Should().Be(TradingDayStatus.Holiday);
        evidence.IsTradingDay.Should().BeFalse();
    }

    [Fact]
    public void EarlyCloseDaysAreTradingDaysWithAnEarlierCloseTime()
    {
        var evidence = ExchangeCalendarCatalog.GetTradingDay(
            MarketRegion.UnitedStates, new DateOnly(2026, 11, 27));

        evidence.Status.Should().Be(TradingDayStatus.EarlyClose);
        evidence.IsTradingDay.Should().BeTrue();
        evidence.EarlyCloseTime.Should().Be(new TimeSpan(13, 0, 0));

        ExchangeCalendarCatalog.ResolveCloseTime(
            MarketRegion.UnitedStates, new DateOnly(2026, 11, 27))
            .Should().Be(new TimeSpan(13, 0, 0));
    }

    [Fact]
    public void OrdinaryTradingDaysUseTheRegularSessionClose()
    {
        var regularClose = MarketRegionCatalog.Get(MarketRegion.UnitedStates).RegularClose;

        ExchangeCalendarCatalog.ResolveCloseTime(
            MarketRegion.UnitedStates, new DateOnly(2026, 8, 18))
            .Should().Be(regularClose);
    }

    [Fact]
    public void WeekendsRemainNonTradingDays()
    {
        var saturday = new DateOnly(2026, 8, 22);
        saturday.DayOfWeek.Should().Be(DayOfWeek.Saturday);

        ExchangeCalendarCatalog.GetTradingDay(MarketRegion.UnitedStates, saturday)
            .Status.Should().Be(TradingDayStatus.Weekend);
    }

    [Theory]
    [InlineData(2023, 12, 31)]  // 보유 범위 이전
    [InlineData(2028, 1, 1)]    // 보유 범위 이후
    public void DatesOutsideCoverageFailClosedInsteadOfGuessing(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);

        ExchangeCalendarCatalog.CoversDate(date).Should().BeFalse();

        var act = () => ExchangeCalendarCatalog.GetTradingDay(MarketRegion.UnitedStates, date);

        act.Should().Throw<MarketCalendarCoverageException>()
            .Which.RequestedDate.Should().Be(date);
    }

    [Fact]
    public void CalendarVersionIsStatedSoResultsCanRecordTheirEvidence()
    {
        ExchangeCalendarCatalog.Version.Should().Be(MarketCalendarVersion.Current);
        ExchangeCalendarCatalog.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NoDeclaredHolidayFallsOnAWeekend()
    {
        // 주말에 오는 휴장일을 목록에 넣으면 실제 거래일 수를 왜곡하지 않지만,
        // 관측 규칙(대체 휴일)을 잘못 적용했다는 신호이므로 목록을 깨끗하게 유지한다.
        foreach (var market in new[] { MarketRegion.UnitedStates, MarketRegion.Korea })
        {
            for (var date = new DateOnly(2024, 1, 1);
                 date <= new DateOnly(2027, 12, 31);
                 date = date.AddDays(1))
            {
                var evidence = ExchangeCalendarCatalog.GetTradingDay(market, date);

                if (evidence.Status != TradingDayStatus.Holiday)
                    continue;

                date.DayOfWeek.Should().NotBe(DayOfWeek.Saturday,
                    $"{market} {date:yyyy-MM-dd} 휴장일이 토요일에 선언되었습니다");
                date.DayOfWeek.Should().NotBe(DayOfWeek.Sunday,
                    $"{market} {date:yyyy-MM-dd} 휴장일이 일요일에 선언되었습니다");
            }
        }
    }
}

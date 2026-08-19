using FluentAssertions;
using StockTrader.Application.Analysis;
using StockTrader.Domain.MarketData;
using TimeZoneConverter;

namespace StockTrader.Tests;

public sealed class MlRetrainingSchedulePolicyTests
{
    private static readonly TimeZoneInfo Eastern =
        TZConvert.GetTimeZoneInfo("America/New_York");
    private static readonly TimeOnly RetrainAfter = new(17, 0);

    /// <summary>실제 거래소 캘린더를 연결해 휴장일 동작까지 함께 고정한다.</summary>
    private static bool IsTradingDay(DateOnly date) =>
        ExchangeCalendarCatalog.GetTradingDay(MarketRegion.UnitedStates, date).IsTradingDay;

    [Fact]
    public void WeekdayWindowUsesExplicitObservationTime()
    {
        var beforeWindow = new DateTimeOffset(2026, 8, 19, 20, 30, 0, TimeSpan.Zero);
        var afterWindow = beforeWindow.AddHours(1);

        MlRetrainingSchedulePolicy.Evaluate(beforeWindow, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(MlRetrainingWindowStatus.BeforeDailyWindow);
        MlRetrainingSchedulePolicy.CalculateInitialDelay(beforeWindow, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(TimeSpan.FromMinutes(30));
        MlRetrainingSchedulePolicy.Evaluate(afterWindow, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(MlRetrainingWindowStatus.Eligible);
        MlRetrainingSchedulePolicy.CalculateInitialDelay(afterWindow, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void WeekendDelayTargetsMondayWindowAcrossDaylightSavingChange()
    {
        // Saturday noon EST, immediately before the 2026 US daylight-saving transition.
        var observedAt = new DateTimeOffset(2026, 3, 7, 17, 0, 0, TimeSpan.Zero);

        var delay = MlRetrainingSchedulePolicy.CalculateInitialDelay(
            observedAt,
            Eastern,
            RetrainAfter,
            IsTradingDay);

        MlRetrainingSchedulePolicy.Evaluate(observedAt, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(MlRetrainingWindowStatus.NonTradingDay);
        observedAt.Add(delay).Should().Be(
            new DateTimeOffset(2026, 3, 9, 21, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void RecurringDailyDelayReanchorsAfterFallDaylightSavingChange()
    {
        // Friday 17:00 EDT. A raw 24-hour timer would become 16:00 EST after the fallback.
        var previousRun = new DateTimeOffset(2026, 10, 30, 21, 0, 0, TimeSpan.Zero);

        var delay = MlRetrainingSchedulePolicy.CalculateRecurringDelay(
            previousRun,
            TimeSpan.FromHours(24),
            Eastern,
            RetrainAfter,
            IsTradingDay);

        previousRun.Add(delay).Should().Be(
            new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
        MlRetrainingSchedulePolicy.Evaluate(
                previousRun.Add(delay),
                Eastern,
                RetrainAfter,
                IsTradingDay)
            .Should().Be(MlRetrainingWindowStatus.Eligible);
    }

    [Fact]
    public void ExchangeHolidayIsSkippedLikeAWeekend()
    {
        // 2026-11-26 목요일 추수감사절 18:00 ET. 주말 전용 규칙은 이 날을 재학습 대상으로
        // 판정했지만, 새로 완성된 거래가 없으므로 재학습할 근거가 없다.
        var thanksgiving = new DateTimeOffset(2026, 11, 26, 23, 0, 0, TimeSpan.Zero);

        MlRetrainingSchedulePolicy.Evaluate(thanksgiving, Eastern, RetrainAfter, IsTradingDay)
            .Should().Be(MlRetrainingWindowStatus.NonTradingDay);

        // 다음 거래일은 조기마감일인 11-27 금요일이며, 재학습 창은 그날 17:00 ET 이다.
        var delay = MlRetrainingSchedulePolicy.CalculateInitialDelay(
            thanksgiving, Eastern, RetrainAfter, IsTradingDay);

        thanksgiving.Add(delay).Should().Be(
            new DateTimeOffset(2026, 11, 27, 22, 0, 0, TimeSpan.Zero));
    }
}

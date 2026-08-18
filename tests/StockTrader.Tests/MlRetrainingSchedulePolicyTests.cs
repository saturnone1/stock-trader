using FluentAssertions;
using StockTrader.Application.Analysis;
using TimeZoneConverter;

namespace StockTrader.Tests;

public sealed class MlRetrainingSchedulePolicyTests
{
    private static readonly TimeZoneInfo Eastern =
        TZConvert.GetTimeZoneInfo("America/New_York");
    private static readonly TimeOnly RetrainAfter = new(17, 0);

    [Fact]
    public void WeekdayWindowUsesExplicitObservationTime()
    {
        var beforeWindow = new DateTimeOffset(2026, 8, 19, 20, 30, 0, TimeSpan.Zero);
        var afterWindow = beforeWindow.AddHours(1);

        MlRetrainingSchedulePolicy.Evaluate(beforeWindow, Eastern, RetrainAfter)
            .Should().Be(MlRetrainingWindowStatus.BeforeDailyWindow);
        MlRetrainingSchedulePolicy.CalculateInitialDelay(beforeWindow, Eastern, RetrainAfter)
            .Should().Be(TimeSpan.FromMinutes(30));
        MlRetrainingSchedulePolicy.Evaluate(afterWindow, Eastern, RetrainAfter)
            .Should().Be(MlRetrainingWindowStatus.Eligible);
        MlRetrainingSchedulePolicy.CalculateInitialDelay(afterWindow, Eastern, RetrainAfter)
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
            RetrainAfter);

        MlRetrainingSchedulePolicy.Evaluate(observedAt, Eastern, RetrainAfter)
            .Should().Be(MlRetrainingWindowStatus.Weekend);
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
            RetrainAfter);

        previousRun.Add(delay).Should().Be(
            new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero));
        MlRetrainingSchedulePolicy.Evaluate(
                previousRun.Add(delay),
                Eastern,
                RetrainAfter)
            .Should().Be(MlRetrainingWindowStatus.Eligible);
    }
}

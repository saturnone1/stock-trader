namespace StockTrader.Application.Analysis;

public enum MlRetrainingWindowStatus
{
    Eligible,
    Weekend,
    BeforeDailyWindow
}

public static class MlRetrainingSchedulePolicy
{
    public static MlRetrainingWindowStatus Evaluate(
        DateTimeOffset observedAtUtc,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter)
    {
        var local = TimeZoneInfo.ConvertTime(observedAtUtc, marketTimeZone);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return MlRetrainingWindowStatus.Weekend;
        return TimeOnly.FromDateTime(local.DateTime) >= retrainAfter
            ? MlRetrainingWindowStatus.Eligible
            : MlRetrainingWindowStatus.BeforeDailyWindow;
    }

    public static TimeSpan CalculateInitialDelay(
        DateTimeOffset observedAtUtc,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter)
    {
        if (Evaluate(observedAtUtc, marketTimeZone, retrainAfter)
            == MlRetrainingWindowStatus.Eligible)
        {
            return TimeSpan.FromMinutes(1);
        }

        var local = TimeZoneInfo.ConvertTime(observedAtUtc, marketTimeZone);
        var nextWindow = DateTime.SpecifyKind(
            local.Date + retrainAfter.ToTimeSpan(),
            DateTimeKind.Unspecified);
        if (TimeOnly.FromDateTime(local.DateTime) >= retrainAfter)
            nextWindow = nextWindow.AddDays(1);
        while (nextWindow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            nextWindow = nextWindow.AddDays(1);

        var nextWindowUtc = TimeZoneInfo.ConvertTimeToUtc(nextWindow, marketTimeZone);
        var delay = nextWindowUtc - observedAtUtc.UtcDateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay;
    }

    public static TimeSpan CalculateRecurringDelay(
        DateTimeOffset observedAtUtc,
        TimeSpan interval,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        var intervalCandidate = observedAtUtc.Add(interval);
        if (Evaluate(intervalCandidate, marketTimeZone, retrainAfter)
            == MlRetrainingWindowStatus.Eligible)
        {
            return interval;
        }

        return interval + CalculateInitialDelay(
            intervalCandidate,
            marketTimeZone,
            retrainAfter);
    }
}

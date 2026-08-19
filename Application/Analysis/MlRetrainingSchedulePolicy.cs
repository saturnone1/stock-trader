namespace StockTrader.Application.Analysis;

public enum MlRetrainingWindowStatus
{
    Eligible,

    /// <summary>주말·거래소 휴장일. 새로 완성된 거래 결과가 없어 재학습할 근거가 없다.</summary>
    NonTradingDay,

    BeforeDailyWindow
}

public static class MlRetrainingSchedulePolicy
{
    public static MlRetrainingWindowStatus Evaluate(
        DateTimeOffset observedAtUtc,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter,
        Func<DateOnly, bool> isMarketTradingDay)
    {
        var local = TimeZoneInfo.ConvertTime(observedAtUtc, marketTimeZone);
        if (!isMarketTradingDay(DateOnly.FromDateTime(local.DateTime)))
            return MlRetrainingWindowStatus.NonTradingDay;
        return TimeOnly.FromDateTime(local.DateTime) >= retrainAfter
            ? MlRetrainingWindowStatus.Eligible
            : MlRetrainingWindowStatus.BeforeDailyWindow;
    }

    public static TimeSpan CalculateInitialDelay(
        DateTimeOffset observedAtUtc,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter,
        Func<DateOnly, bool> isMarketTradingDay)
    {
        if (Evaluate(observedAtUtc, marketTimeZone, retrainAfter, isMarketTradingDay)
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

        // 어떤 시장도 연속 휴장이 이보다 길지 않다. 판정이 계속 거짓이어도 유한하게 끝낸다.
        const int maximumConsecutiveNonTradingDays = 14;
        for (var attempt = 0;
             attempt < maximumConsecutiveNonTradingDays
                 && !isMarketTradingDay(DateOnly.FromDateTime(nextWindow));
             attempt++)
        {
            nextWindow = nextWindow.AddDays(1);
        }

        var nextWindowUtc = TimeZoneInfo.ConvertTimeToUtc(nextWindow, marketTimeZone);
        var delay = nextWindowUtc - observedAtUtc.UtcDateTime;
        return delay < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay;
    }

    public static TimeSpan CalculateRecurringDelay(
        DateTimeOffset observedAtUtc,
        TimeSpan interval,
        TimeZoneInfo marketTimeZone,
        TimeOnly retrainAfter,
        Func<DateOnly, bool> isMarketTradingDay)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        var intervalCandidate = observedAtUtc.Add(interval);
        if (Evaluate(intervalCandidate, marketTimeZone, retrainAfter, isMarketTradingDay)
            == MlRetrainingWindowStatus.Eligible)
        {
            return interval;
        }

        return interval + CalculateInitialDelay(
            intervalCandidate,
            marketTimeZone,
            retrainAfter,
            isMarketTradingDay);
    }
}

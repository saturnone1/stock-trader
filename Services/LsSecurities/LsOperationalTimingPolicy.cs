namespace StockTrader.Services.LsSecurities;

internal static class LsOperationalTimingPolicy
{
    // LS OAuth access tokens are valid until 07:00 KST on the day after issuance.
    public static readonly TimeOnly DailyTokenExpiryKst = new(7, 0);

    // LS chart transaction requests are limited to one request per second.
    public static readonly TimeSpan MinimumChartRequestInterval = TimeSpan.FromSeconds(1);

    public static DateTimeOffset CalculateTokenExpiryUtc(
        DateTimeOffset observedAtUtc,
        TimeZoneInfo marketTimeZone,
        TimeOnly dailyExpiry,
        TimeSpan safetyMargin)
    {
        if (safetyMargin < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(safetyMargin));

        var local = TimeZoneInfo.ConvertTime(observedAtUtc, marketTimeZone);
        var expiryLocal = DateTime.SpecifyKind(
            local.Date + dailyExpiry.ToTimeSpan(),
            DateTimeKind.Unspecified);
        var safeExpiryLocal = expiryLocal - safetyMargin;
        if (local.DateTime >= safeExpiryLocal)
            expiryLocal = expiryLocal.AddDays(1);

        var expiryUtc = TimeZoneInfo.ConvertTimeToUtc(expiryLocal, marketTimeZone);
        return new DateTimeOffset(expiryUtc, TimeSpan.Zero) - safetyMargin;
    }

    public static TimeSpan CalculateRateLimitDelay(
        TimeSpan? elapsedSincePreviousRequest,
        TimeSpan minimumInterval)
    {
        if (minimumInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        if (!elapsedSincePreviousRequest.HasValue)
            return TimeSpan.Zero;

        var remaining = minimumInterval - elapsedSincePreviousRequest.Value;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

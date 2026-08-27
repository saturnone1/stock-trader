namespace StockTrader.Domain.MarketData;

public sealed record ExchangeSessionDecision(
    bool IsOpen,
    string Reason,
    DateTime LocalTime,
    string CalendarVersion);

/// <summary>
/// Deterministic regular-session gate shared by Local and Trading Core. Calendar coverage is an
/// explicit fail-closed input; system time remains outside this policy.
/// </summary>
public static class ExchangeSessionPolicy
{
    public static ExchangeSessionDecision Evaluate(MarketRegion market, DateTime observedAtUtc)
    {
        var utc = observedAtUtc.Kind switch
        {
            DateTimeKind.Utc => observedAtUtc,
            DateTimeKind.Local => observedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(observedAtUtc, DateTimeKind.Utc),
        };
        var descriptor = MarketRegionCatalog.Get(market);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(descriptor.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        TradingDayEvidence day;
        try
        {
            day = ExchangeCalendarCatalog.GetTradingDay(market, DateOnly.FromDateTime(local));
        }
        catch (MarketCalendarCoverageException)
        {
            return new(false, "market-calendar-coverage-missing", local,
                ExchangeCalendarCatalog.Version);
        }
        if (!day.IsTradingDay)
            return new(false, "market-closed-trading-day", local, ExchangeCalendarCatalog.Version);
        var close = day.EarlyCloseTime ?? descriptor.RegularClose;
        var open = local.TimeOfDay >= descriptor.RegularOpen && local.TimeOfDay <= close;
        return new(open, open ? string.Empty : "market-outside-regular-session", local,
            ExchangeCalendarCatalog.Version);
    }
}

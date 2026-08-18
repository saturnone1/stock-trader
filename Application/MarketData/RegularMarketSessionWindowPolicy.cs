namespace StockTrader.Application.MarketData;

public sealed record RegularMarketSessionWindow(
    DateTime StartUtc,
    DateTime EndUtc);

/// <summary>
/// A market-local trading date and regular-session definition are converted into an exact UTC
/// provider request window. The supplied time zone owns daylight-saving transitions.
/// </summary>
public static class RegularMarketSessionWindowPolicy
{
    public static RegularMarketSessionWindow Resolve(
        DateOnly marketDate,
        TimeZoneInfo marketTimeZone,
        TimeSpan regularOpen,
        TimeSpan regularClose)
    {
        ArgumentNullException.ThrowIfNull(marketTimeZone);
        if (regularOpen < TimeSpan.Zero || regularOpen >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(regularOpen));
        if (regularClose <= regularOpen || regularClose > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(regularClose));

        var localDate = DateTime.SpecifyKind(
            marketDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            localDate.Add(regularOpen),
            marketTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            localDate.Add(regularClose),
            marketTimeZone);

        return new RegularMarketSessionWindow(startUtc, endUtc);
    }
}

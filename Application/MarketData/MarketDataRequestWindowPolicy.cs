namespace StockTrader.Application.MarketData;

public sealed record MarketDataRequestWindow(
    DateTime StartUtc,
    DateTime EndUtc);

/// <summary>
/// Normalizes provider request boundaries without reinterpreting an existing UTC instant through
/// the host's local time zone. Unspecified values use the application's UTC interval convention.
/// </summary>
public static class MarketDataRequestWindowPolicy
{
    public static MarketDataRequestWindow Resolve(DateTime from, DateTime to)
    {
        var startUtc = NormalizeUtc(from);
        var endUtc = NormalizeUtc(to);
        if (startUtc >= endUtc)
        {
            throw new ArgumentException(
                "Market-data request start must be earlier than its end.",
                nameof(from));
        }

        return new MarketDataRequestWindow(startUtc, endUtc);
    }

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

using System.Globalization;
using System.Text.Json;

namespace StockTrader.Services.Broker;

internal static class LsOrderTimestampParser
{
    public static bool TryParseUtc(
        JsonElement order,
        DateTime requestedDate,
        TimeZoneInfo marketTimeZone,
        out DateTime submittedAtUtc)
    {
        submittedAtUtc = default;
        var date = requestedDate.Date;
        if (TryReadText(order, "OrdDt", out var dateText)
            && !DateTime.TryParseExact(
                dateText,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return false;
        }

        if (!TryReadText(order, "OrdTime", out var timeText)
            || timeText.Length < 6
            || !TimeOnly.TryParseExact(
                timeText[..6],
                "HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return false;
        }

        var milliseconds = 0;
        if (timeText.Length > 6)
        {
            var fraction = timeText[6..];
            if (fraction.Length > 3)
                fraction = fraction[..3];
            fraction = fraction.PadRight(3, '0');
            if (!int.TryParse(
                    fraction,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out milliseconds))
            {
                return false;
            }
        }

        var local = DateTime.SpecifyKind(
            date.Date + time.ToTimeSpan() + TimeSpan.FromMilliseconds(milliseconds),
            DateTimeKind.Unspecified);
        if (marketTimeZone.IsInvalidTime(local))
            return false;

        submittedAtUtc = TimeZoneInfo.ConvertTimeToUtc(local, marketTimeZone);
        return true;
    }

    private static bool TryReadText(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
            return false;
        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty,
        };
        return !string.IsNullOrWhiteSpace(value);
    }
}

namespace StockTrader.Services.Broker;

/// <summary>UTC 주문 조회 구간을 LS의 한국 거래일 요청 범위로 변환합니다.</summary>
internal static class LsOrderHistoryWindow
{
    public static IReadOnlyList<DateOnly> KoreanTradingDates(
        DateTime fromUtc,
        DateTime toUtc,
        TimeZoneInfo koreanTimeZone)
    {
        fromUtc = NormalizeUtc(fromUtc);
        toUtc = NormalizeUtc(toUtc);
        if (fromUtc > toUtc) return [];

        var fromDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(fromUtc, koreanTimeZone));
        var toDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(toUtc, koreanTimeZone));
        var dates = new List<DateOnly>();
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            dates.Add(date);
        return dates;
    }

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

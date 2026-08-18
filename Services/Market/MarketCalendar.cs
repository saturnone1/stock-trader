using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using TimeZoneConverter;

namespace StockTrader.Services.Market;

/// <summary>
/// US(NYSE/NASDAQ)와 KRX 시장 시간을 통합 관리하는 캘린더 서비스.
/// 주말 휴장은 체크하며, 공휴일은 별도 데이터가 필요하므로 향후 확장.
/// </summary>
public class MarketCalendar : IMarketCalendar
{
    private static readonly IReadOnlyDictionary<MarketRegion, TimeZoneInfo> TimeZones =
        MarketRegionCatalog.All.ToDictionary(
            item => item.Value,
            item => TZConvert.GetTimeZoneInfo(item.TimeZoneId));

    private readonly TimeProvider _timeProvider;

    public MarketCalendar(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public bool IsMarketOpen(MarketRegion market)
    {
        var now = GetLocalNow(market);

        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var open = GetMarketOpen(market);
        var close = GetMarketClose(market);
        return now.TimeOfDay >= open && now.TimeOfDay <= close;
    }

    public TimeZoneInfo GetTimeZone(MarketRegion market) =>
        TimeZones.TryGetValue(market, out var timeZone)
            ? timeZone
            : throw new ArgumentOutOfRangeException(
                nameof(market), market, "지원하지 않는 시장입니다.");

    public TimeSpan GetMarketOpen(MarketRegion market) =>
        MarketRegionCatalog.Get(market).RegularOpen;

    public TimeSpan GetMarketClose(MarketRegion market) =>
        MarketRegionCatalog.Get(market).RegularClose;

    public DateTime GetLocalNow(MarketRegion market) =>
        GetLocalTime(market, _timeProvider.GetUtcNow().UtcDateTime);

    public DateTime GetLocalTime(MarketRegion market, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            GetTimeZone(market));

}

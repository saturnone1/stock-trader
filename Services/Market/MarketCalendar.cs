using TimeZoneConverter;

namespace StockTrader.Services.Market;

/// <summary>
/// US(NYSE/NASDAQ)와 KRX 시장 시간을 통합 관리하는 캘린더 서비스.
/// 주말 휴장은 체크하며, 공휴일은 별도 데이터가 필요하므로 향후 확장.
/// </summary>
public class MarketCalendar : IMarketCalendar
{
    private readonly TimeProvider _timeProvider;

    public MarketCalendar(TimeProvider timeProvider) => _timeProvider = timeProvider;

    private static readonly TimeZoneInfo EasternTime =
        TZConvert.GetTimeZoneInfo("America/New_York");

    private static readonly TimeZoneInfo KoreaTime =
        TZConvert.GetTimeZoneInfo("Asia/Seoul");

    // US: 09:30 ~ 16:00 ET
    private static readonly TimeSpan UsOpen = new(9, 30, 0);
    private static readonly TimeSpan UsClose = new(16, 0, 0);

    // KRX: 09:00 ~ 15:30 KST
    private static readonly TimeSpan KrxOpen = new(9, 0, 0);
    private static readonly TimeSpan KrxClose = new(15, 30, 0);

    public bool IsMarketOpen(MarketType market)
    {
        var now = GetLocalNow(market);

        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var open = GetMarketOpen(market);
        var close = GetMarketClose(market);
        return now.TimeOfDay >= open && now.TimeOfDay <= close;
    }

    public TimeZoneInfo GetTimeZone(MarketType market) => market switch
    {
        MarketType.KRX => KoreaTime,
        _ => EasternTime
    };

    public TimeSpan GetMarketOpen(MarketType market) => market switch
    {
        MarketType.KRX => KrxOpen,
        _ => UsOpen
    };

    public TimeSpan GetMarketClose(MarketType market) => market switch
    {
        MarketType.KRX => KrxClose,
        _ => UsClose
    };

    public DateTime GetLocalNow(MarketType market)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, GetTimeZone(market));
    }

}

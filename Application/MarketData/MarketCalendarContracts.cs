using StockTrader.Domain.MarketData;

namespace StockTrader.Application.MarketData;

/// <summary>시장 시간대와 정규장 경계를 현재 시각에 적용하는 포트입니다.</summary>
public interface IMarketCalendar
{
    bool IsMarketOpen(MarketRegion market);
    TimeZoneInfo GetTimeZone(MarketRegion market);
    TimeSpan GetMarketOpen(MarketRegion market);
    TimeSpan GetMarketClose(MarketRegion market);
    DateTime GetLocalNow(MarketRegion market);
    DateTime GetLocalTime(MarketRegion market, DateTime utc);
}

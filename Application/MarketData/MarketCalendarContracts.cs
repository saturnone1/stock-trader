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

    /// <summary>
    /// 해당 시장 로컬 날짜의 거래 상태. 휴장·조기마감 근거를 포함한다.
    /// 캘린더 근거가 없는 날짜는 추측하지 않고 실패한다.
    /// </summary>
    TradingDayEvidence GetTradingDay(MarketRegion market, DateOnly marketDate);
}

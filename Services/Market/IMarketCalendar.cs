namespace StockTrader.Services.Market;

/// <summary>
/// 시장 캘린더 서비스. US(NYSE/NASDAQ)와 KRX(한국거래소) 시장 시간을 통합 관리.
/// </summary>
public interface IMarketCalendar
{
    /// <summary>현재 시장이 열려 있는지 여부</summary>
    bool IsMarketOpen(MarketType market);

    /// <summary>해당 시장의 타임존 정보</summary>
    TimeZoneInfo GetTimeZone(MarketType market);

    /// <summary>해당 시장의 장 시작 시간 (현지 시간 기준)</summary>
    TimeSpan GetMarketOpen(MarketType market);

    /// <summary>해당 시장의 장 종료 시간 (현지 시간 기준)</summary>
    TimeSpan GetMarketClose(MarketType market);

    /// <summary>해당 시장 기준 현재 현지 시간</summary>
    DateTime GetLocalNow(MarketType market);
}

public enum MarketType
{
    /// <summary>US NYSE/NASDAQ — ET 09:30~16:00</summary>
    US,

    /// <summary>한국거래소 KRX — KST 09:00~15:30</summary>
    KRX
}

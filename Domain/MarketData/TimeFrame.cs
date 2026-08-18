namespace StockTrader.Domain.MarketData;

/// <summary>
/// 시장 데이터와 전략 실행이 공유하는 봉 주기 식별자다.
/// 직렬화와 데이터베이스 호환성을 위해 선언 순서를 유지한다.
/// </summary>
public enum TimeFrame
{
    OneMinute,
    FiveMinute,
    FifteenMinute,
    Daily,
    Weekly
}

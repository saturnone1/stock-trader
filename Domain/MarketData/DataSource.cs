namespace StockTrader.Domain.MarketData;

/// <summary>
/// 시장 데이터 공급자 식별자다. 공급자 구현 세부사항은 Infrastructure에 남는다.
/// 직렬화와 데이터베이스 호환성을 위해 선언 순서를 유지한다.
/// </summary>
public enum DataSource
{
    Alpaca,
    Polygon,
    Yahoo,
    LsSecurities
}

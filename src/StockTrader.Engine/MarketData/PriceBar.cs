namespace StockTrader.Engine.MarketData;

/// <summary>
/// 결정론 엔진이 소비하는 저장소 독립 가격봉입니다. 데이터베이스 식별자와 공급자 객체를
/// 포함하지 않으며, 시간축과 OHLCV 값만으로 계산 결과가 결정됩니다.
/// </summary>
public readonly record struct PriceBar(
    DateTime Timestamp,
    TimeFrame TimeFrame,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Vwap = null);

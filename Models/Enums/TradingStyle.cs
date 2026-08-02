namespace StockTrader.Models.Enums;

/// <summary>
/// 매매 전략의 트레이딩 스타일 분류
/// </summary>
public enum TradingStyle
{
    Scalping,       // 스캘핑 (초단타, 수분~수시간)
    DayTrading,     // 데이트레이딩 (단타, 당일~1-2일)
    Swing,          // 스윙 (수일~수주)
    Position,       // 포지션 (장기, 수주~수개월)
    Filter          // 필터 전용 (독립 시그널 없음)
}

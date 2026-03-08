using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// 브로커 주문 정보 — 브로커 독립적인 도메인 모델.
/// 각 IBrokerService 구현체가 브로커별 주문 응답을 이 모델로 변환한다.
/// </summary>
public class BrokerOrder
{
    /// <summary>브로커가 부여한 주문 ID (문자열 형태로 통일)</summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>종목 코드</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>매수/매도 방향</summary>
    public TradeDirection Direction { get; set; }

    /// <summary>주문 수량</summary>
    public int Quantity { get; set; }

    /// <summary>체결 수량 (부분 체결 포함)</summary>
    public int FilledQuantity { get; set; }

    /// <summary>주문 가격 (시장가이면 null)</summary>
    public decimal? OrderPrice { get; set; }

    /// <summary>평균 체결 단가</summary>
    public decimal? AverageFillPrice { get; set; }

    /// <summary>주문 상태</summary>
    public BrokerOrderStatus Status { get; set; }

    /// <summary>주문 유형</summary>
    public BrokerOrderType OrderType { get; set; }

    /// <summary>주문 제출 시각 (UTC)</summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>최종 체결 시각 (UTC)</summary>
    public DateTime? FilledAt { get; set; }
}

/// <summary>브로커 독립적인 주문 상태</summary>
public enum BrokerOrderStatus
{
    Pending,
    Accepted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
    Expired,
    Unknown
}

/// <summary>브로커 독립적인 주문 유형</summary>
public enum BrokerOrderType
{
    Market,
    Limit,
    StopLimit,
    BracketOrder,
    Unknown
}

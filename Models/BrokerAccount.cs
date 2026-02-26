namespace StockTrader.Models;

/// <summary>
/// 브로커 계좌 정보 — 브로커 독립적인 도메인 모델.
/// 각 IBrokerService 구현체가 브로커별 응답을 이 모델로 변환한다.
/// </summary>
public class BrokerAccount
{
    /// <summary>계좌 식별자 (브로커별 고유 ID)</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>총 자산 (현금 + 주식 평가액)</summary>
    public decimal TotalEquity { get; set; }

    /// <summary>현금 잔고</summary>
    public decimal Cash { get; set; }

    /// <summary>구매 가능 금액 (매수 여력)</summary>
    public decimal BuyingPower { get; set; }

    /// <summary>미실현 손익</summary>
    public decimal UnrealizedPnL { get; set; }

    /// <summary>일일 실현 손익</summary>
    public decimal DailyPnL { get; set; }

    /// <summary>매매 정지 여부 (PDT, 리스크 한도 초과 등)</summary>
    public bool IsTradingBlocked { get; set; }

    /// <summary>계좌 상태 메시지</summary>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>조회 시각 (UTC)</summary>
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

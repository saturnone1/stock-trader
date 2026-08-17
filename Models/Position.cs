namespace StockTrader.Models;

public class Position
{
    public long Id { get; set; }

    /// <summary>
    /// 이 포지션을 소유하는 TradingAccount.Id.
    /// 0은 계좌 미지정(레거시 데이터 또는 단일 계좌 운용).
    /// </summary>
    public int AccountId { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal? ExitPrice { get; set; }

    /// <summary>진입 이후 최고가 (트레일링 스탑용). 0이면 미추적.</summary>
    public decimal HighSinceEntry { get; set; }

    /// <summary>진입 시 ATR 값 (트레일링/손익비 계산용).</summary>
    public decimal EntryAtr { get; set; }

    /// <summary>진입 시 확정된 1주당 위험거리. 보호 손절이 움직여도 변경하지 않는다.</summary>
    public decimal InitialRiskDistance { get; set; }

    /// <summary>손익분기 손절이 적용됐는지 여부. 서비스 재시작 후에도 유지한다.</summary>
    public bool BreakevenApplied { get; set; }

    /// <summary>추적손절이 활성화됐는지 여부. 서비스 재시작 후에도 유지한다.</summary>
    public bool TrailingStopActivated { get; set; }

    /// <summary>브로커 청산 주문 전에 기록하는 내구성 있는 주문 의도 시각.</summary>
    public DateTime? ExitRequestedAt { get; set; }

    /// <summary>청산 의도를 발생시킨 전략 사유.</summary>
    public string? ExitRequestReason { get; set; }

    /// <summary>브로커가 반환한 청산 주문 ID. 재시작 후 체결 재조정에 사용한다.</summary>
    public string? ExitOrderId { get; set; }

    public bool IsOpen => ClosedAt == null;
    public decimal UnrealizedPnL => (CurrentPrice - EntryPrice) * Quantity;
    public decimal? RealizedPnL => ExitPrice.HasValue
        ? (ExitPrice.Value - EntryPrice) * Quantity
        : null;
}

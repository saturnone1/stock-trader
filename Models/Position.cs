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
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal? ExitPrice { get; set; }

    /// <summary>진입 이후 최고가 (트레일링 스탑용). 0이면 미추적.</summary>
    public decimal HighSinceEntry { get; set; }

    /// <summary>진입 시 ATR 값 (트레일링/손익비 계산용).</summary>
    public decimal EntryAtr { get; set; }

    public bool IsOpen => ClosedAt == null;
    public decimal UnrealizedPnL => (CurrentPrice - EntryPrice) * Quantity;
    public decimal? RealizedPnL => ExitPrice.HasValue
        ? (ExitPrice.Value - EntryPrice) * Quantity
        : null;
}

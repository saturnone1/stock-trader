using System.ComponentModel.DataAnnotations.Schema;

namespace StockTrader.Models;

public class TradeRecord
{
    public long Id { get; set; }
    /// <summary>이 실현 거래를 만든 원래 진입 신호. 부분청산은 같은 값을 공유합니다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long? SourceSignalId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public int Quantity { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public string ExitReason { get; set; } = string.Empty;

    // Adaptive slippage: ATR과 거래량 정보 (백테스트에서만 사용, DB 저장 안 됨)
    [NotMapped]
    public decimal EntryAtr { get; set; }
    [NotMapped]
    public long EntryVolume { get; set; }

    /// <summary>진입 시점의 포트폴리오 자본</summary>
    [NotMapped]
    public decimal EquityAtEntry { get; set; }

    /// <summary>Maximum Adverse Excursion — 진입 후 최대 불리한 움직임 (%)</summary>
    [NotMapped]
    public decimal MaePercent { get; set; }

    /// <summary>Maximum Favorable Excursion — 진입 후 최대 유리한 움직임 (%)</summary>
    [NotMapped]
    public decimal MfePercent { get; set; }

    public bool IsWin => PnL > 0;
}

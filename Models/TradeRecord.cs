using System.ComponentModel.DataAnnotations.Schema;

namespace StockTrader.Models;

public class TradeRecord
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public int Quantity { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public string ExitReason { get; set; } = string.Empty;

    // Adaptive slippage: ATR과 거래량 정보 (백테스트에서만 사용, DB 저장 안 됨)
    [NotMapped] public decimal EntryAtr { get; set; }
    [NotMapped] public long EntryVolume { get; set; }

    public bool IsWin => PnL > 0;
}

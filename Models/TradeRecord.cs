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

    public bool IsWin => PnL > 0;
}

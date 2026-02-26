namespace StockTrader.Models;

public class PatternSignal
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public DateTime DetectedAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal Confidence { get; set; }
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

namespace StockTrader.Models;

public class Position
{
    public long Id { get; set; }
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

    public bool IsOpen => ClosedAt == null;
    public decimal UnrealizedPnL => (CurrentPrice - EntryPrice) * Quantity;
    public decimal? RealizedPnL => ExitPrice.HasValue
        ? (ExitPrice.Value - EntryPrice) * Quantity
        : null;
}

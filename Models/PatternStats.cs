namespace StockTrader.Models;

public class PatternStats
{
    public long Id { get; set; }
    public PatternType PatternType { get; set; }
    public string? Symbol { get; set; }
    public int SampleSize { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgWinPercent { get; set; }
    public decimal AvgLossPercent { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public DateTime LastUpdated { get; set; }

    public decimal Expectancy => WinRate * AvgWinPercent - (1 - WinRate) * AvgLossPercent;
    public decimal ProfitFactor => AvgLossPercent != 0
        ? (WinRate * AvgWinPercent) / ((1 - WinRate) * AvgLossPercent)
        : 0;
}

namespace StockTrader.Models;

public class RiskState
{
    public decimal DailyPnL { get; set; }
    public decimal DailyPnLPercent { get; set; }
    public bool IsTradingHalted { get; set; }
    public int OpenPositionCount { get; set; }
    public Dictionary<string, int> PositionsPerSector { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

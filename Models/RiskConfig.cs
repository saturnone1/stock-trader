namespace StockTrader.Models;

public class RiskConfig
{
    public decimal AccountSize { get; set; } = 100_000m;
    public decimal RiskPerTradePercent { get; set; } = 0.01m;
    public decimal DailyLossLimitPercent { get; set; } = 0.03m;
    public int MaxPositionsPerSector { get; set; } = 2;
    public int MaxTotalPositions { get; set; } = 10;
    public decimal MinExpectancy { get; set; } = 0m;
}

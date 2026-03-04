namespace StockTrader.Configuration;

public class TradingSettings
{
    public decimal DefaultAccountSize { get; set; } = 100_000m;
    public decimal RiskPerTradePercent { get; set; } = 0.01m;
    public decimal DailyLossLimitPercent { get; set; } = 0.03m;
    public int MaxPositionsPerSector { get; set; } = 2;
    public int MaxTotalPositions { get; set; } = 7;
    public decimal MinExpectancy { get; set; } = 0m;
    public decimal MinConfidence { get; set; } = 0.3m;
    public int DataFetchIntervalSeconds { get; set; } = 60;
    public int RiskCheckIntervalSeconds { get; set; } = 30;
    public string MarketOpenET { get; set; } = "09:30:00";
    public string MarketCloseET { get; set; } = "16:00:00";
}

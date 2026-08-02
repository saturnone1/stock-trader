namespace StockTrader.Models;

public class Ticker
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public decimal MarketCap { get; set; }
    public bool IsActive { get; set; } = true;
}

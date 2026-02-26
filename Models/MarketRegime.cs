namespace StockTrader.Models;

public class MarketRegime
{
    public bool SpyAbove200Ma { get; set; }
    public decimal SpyPrice { get; set; }
    public decimal Spy200Ma { get; set; }
    public decimal VixLevel { get; set; }
    public string RegimeLabel { get; set; } = "Unknown";
    public DateTime AsOf { get; set; }
}

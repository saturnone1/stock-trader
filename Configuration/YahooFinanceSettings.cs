namespace StockTrader.Configuration;

public class YahooFinanceSettings
{
    public string BaseUrl { get; set; } = "https://query1.finance.yahoo.com";
    public int RateLimitDelayMs { get; set; } = 200;
    public int MaxRetries { get; set; } = 3;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
}

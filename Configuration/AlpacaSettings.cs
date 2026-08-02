namespace StockTrader.Configuration;

public class AlpacaSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://paper-api.alpaca.markets";
    public string DataBaseUrl { get; set; } = "https://data.alpaca.markets";
    public bool IsPaper { get; set; } = true;

    // Streaming
    public bool EnableStreaming { get; set; } = false;
    public List<string> StreamTypes { get; set; } = ["minuteBars"];
    public int MaxReconnectAttempts { get; set; } = 10;
    public int InitialReconnectDelaySeconds { get; set; } = 2;
    public int MaxReconnectDelaySeconds { get; set; } = 300;

    public bool HasConfiguredCredentials =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret) &&
        !ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        !ApiSecret.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
}

namespace StockTrader.Configuration;

public class AlpacaSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool IsPaper { get; set; } = true;
    public bool EnableStreaming { get; set; } = false;

    public bool HasConfiguredCredentials =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ApiSecret) &&
        !ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) &&
        !ApiSecret.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
}

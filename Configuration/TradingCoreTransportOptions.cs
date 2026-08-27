namespace StockTrader.Configuration;

public sealed class TradingCoreTransportOptions
{
    public const string SectionName = "TradingCoreTransport";

    public string Mode { get; set; } = "Local";
    public string Endpoint { get; set; } = "https://stocktrader-trading-core:9443";
    public string SharedSecret { get; set; } = string.Empty;
    public string ClientCertificatePath { get; set; } = string.Empty;
    public string ClientCertificateKeyPath { get; set; } = string.Empty;
    public string ServerCertificateAuthorityPath { get; set; } = string.Empty;
    public string ServerCertificateCommonName { get; set; } = "stocktrader-trading-core";
    public int TimeoutSeconds { get; set; } = 30;
    public int ProjectionIntervalSeconds { get; set; } = 30;
    public int ShadowComparisonIntervalSeconds { get; set; } = 30;

    public bool IsValid()
    {
        if (Mode == "Local") return true;
        return Mode is "Projection" or "Shadow" or "Remote"
            && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(SharedSecret)
            && File.Exists(ClientCertificatePath)
            && File.Exists(ClientCertificateKeyPath)
            && File.Exists(ServerCertificateAuthorityPath)
            && TimeoutSeconds is >= 5 and <= 120
            && ProjectionIntervalSeconds is >= 5 and <= 3600
            && ShadowComparisonIntervalSeconds is >= 5 and <= 3600;
    }
}

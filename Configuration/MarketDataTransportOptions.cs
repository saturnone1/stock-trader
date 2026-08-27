namespace StockTrader.Configuration;

public enum MarketDataTransportMode
{
    Local,
    Shadow,
    Remote
}

public sealed class MarketDataTransportOptions
{
    public const string SectionName = "MarketDataTransport";

    public MarketDataTransportMode Mode { get; init; } = MarketDataTransportMode.Local;
    public Uri Endpoint { get; init; } = new("https://stocktrader-market-data:7443");
    public string ClientCertificatePath { get; init; } = string.Empty;
    public string ClientCertificateKeyPath { get; init; } = string.Empty;
    public string ServerCertificateAuthorityPath { get; init; } = string.Empty;
    public string ServerCertificateCommonName { get; init; } = "stocktrader-market-data";
    public int TimeoutSeconds { get; init; } = 30;
    public int ImportBatchSize { get; init; } = 1000;
    public bool ShadowBackfillEnabled { get; init; }
    public int ShadowBackfillMaxGroups { get; init; } = 500;

    public bool IsValid() => Mode == MarketDataTransportMode.Local ||
        (Endpoint.Scheme == Uri.UriSchemeHttps &&
         File.Exists(ClientCertificatePath) &&
         File.Exists(ClientCertificateKeyPath) &&
         File.Exists(ServerCertificateAuthorityPath) &&
         TimeoutSeconds is >= 1 and <= 300 &&
         ImportBatchSize is >= 100 and <= 10_000 &&
         ShadowBackfillMaxGroups is >= 1 and <= 100_000);
}

namespace StockTrader.Configuration;

public sealed class MlTrainingTransportOptions
{
    public const string SectionName = "MlTrainingTransport";
    public string Mode { get; set; } = "Local";
    public string Endpoint { get; set; } = "https://stocktrader-ml-training:8443";
    public string SharedSecret { get; set; } = string.Empty;
    public string ClientCertificatePath { get; set; } = string.Empty;
    public string ClientCertificateKeyPath { get; set; } = string.Empty;
    public string ServerCertificateAuthorityPath { get; set; } = string.Empty;
    public string ServerCertificateCommonName { get; set; } = "stocktrader-ml-training";
    public int TimeoutSeconds { get; set; } = 600;
    public int PollMilliseconds { get; set; } = 500;

    public bool IsValid()
    {
        if (Mode == "Local") return true;
        return Mode is "Shadow" or "Remote"
            && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(SharedSecret)
            && File.Exists(ClientCertificatePath)
            && File.Exists(ClientCertificateKeyPath)
            && File.Exists(ServerCertificateAuthorityPath)
            && TimeoutSeconds is >= 10 and <= 3600
            && PollMilliseconds is >= 100 and <= 5000;
    }
}

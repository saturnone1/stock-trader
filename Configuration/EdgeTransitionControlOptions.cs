namespace StockTrader.Configuration;

public sealed class EdgeTransitionControlOptions
{
    public const string SectionName = "EdgeTransitionControl";
    public const int InternalPort = 5543;
    public bool Enabled { get; init; }
    public string ServerCertificatePath { get; init; } = string.Empty;
    public string ServerCertificateKeyPath { get; init; } = string.Empty;
    public string ClientCertificateAuthorityPath { get; init; } = string.Empty;
    public string CoordinatorRoleDnsName { get; init; } =
        "trading-cutover-coordinator.stocktrader.internal";

    public bool IsValid() => !Enabled
        || File.Exists(ServerCertificatePath)
        && File.Exists(ServerCertificateKeyPath)
        && File.Exists(ClientCertificateAuthorityPath)
        && !string.IsNullOrWhiteSpace(CoordinatorRoleDnsName);
}

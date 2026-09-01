namespace StockTrader.Configuration;

public sealed class AuthorityCapabilityAttestationOptions
{
    public const string SectionName = "AuthorityCapabilityAttestation";
    public string RuntimeProfile { get; init; } = "api-local";
    public string ImageDigest { get; init; } = string.Empty;
    public string ServiceInventoryHash { get; init; } = string.Empty;
    public string SecretReferenceHash { get; init; } = string.Empty;
    public string NetworkPolicyHash { get; init; } = string.Empty;
    public bool HasBrokerEgress { get; init; }

    public bool IsValid() => !string.IsNullOrWhiteSpace(RuntimeProfile)
        && !string.IsNullOrWhiteSpace(ImageDigest)
        && !string.IsNullOrWhiteSpace(ServiceInventoryHash)
        && !string.IsNullOrWhiteSpace(SecretReferenceHash)
        && !string.IsNullOrWhiteSpace(NetworkPolicyHash);
}

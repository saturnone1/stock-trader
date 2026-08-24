using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal sealed class TradingCoreControlPlaneClient : ITradingCoreControlPlane, IDisposable
{
    private readonly HttpClient? _client;

    public TradingCoreControlPlaneClient(IOptions<TradingCoreTransportOptions> options)
    {
        var settings = options.Value;
        if (settings.Mode != "Local")
        {
            _client = CreateClient(settings);
            _client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        }
    }

    public async Task<bool> PublishProjectionAsync(
        TradingStateSnapshot snapshot,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync("/v1/projections", snapshot, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectionReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-projection-receipt");
        if (!string.Equals(result.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal))
            throw new InvalidOperationException("trading-core-projection-identity-mismatch");
        return result.AlreadyApplied;
    }

    public async Task<bool> PublishAccountConfigurationAsync(
        TradingAccountConfigurationSet configuration,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync(
            "/v1/account-configurations", configuration, ct);
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content
            .ReadFromJsonAsync<TradingAccountConfigurationReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-account-receipt");
        if (receipt.Generation != configuration.Generation
            || !string.Equals(receipt.ConfigurationHash, configuration.ConfigurationHash,
                StringComparison.Ordinal))
            throw new InvalidOperationException("trading-core-account-identity-mismatch");
        return receipt.AlreadyApplied;
    }

    public async Task<TradingCoreStatus> GetStatusAsync(CancellationToken ct = default) =>
        await (_client ?? throw new InvalidOperationException("trading-core-client-disabled"))
            .GetFromJsonAsync<TradingCoreStatus>("/v1/status", ct)
        ?? throw new InvalidOperationException("empty-trading-core-status");

    private static HttpClient CreateClient(TradingCoreTransportOptions options)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(X509Certificate2.CreateFromPemFile(
            options.ClientCertificatePath, options.ClientCertificateKeyPath));
        var serverAuthority = X509Certificate2.CreateFromPem(
            File.ReadAllText(options.ServerCertificateAuthorityPath));
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(serverAuthority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate)
                && certificate.GetNameInfo(X509NameType.DnsName, false)
                    .Equals(options.ServerCertificateCommonName, StringComparison.Ordinal);
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.Endpoint) };
        client.DefaultRequestHeaders.Add("X-StockTrader-Trading-Core-Secret", options.SharedSecret);
        return client;
    }

    public void Dispose() => _client?.Dispose();

    private sealed record ProjectionReceipt(string SnapshotId, bool AlreadyApplied);
}

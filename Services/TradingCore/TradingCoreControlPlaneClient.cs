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

    public async Task<TradingCorePortfolioView> GetPortfolioAsync(CancellationToken ct = default) =>
        await (_client ?? throw new InvalidOperationException("trading-core-client-disabled"))
            .GetFromJsonAsync<TradingCorePortfolioView>("/v1/portfolio", ct)
        ?? throw new InvalidOperationException("empty-trading-core-portfolio");

    public async Task<TradingShadowDecisionReceipt> CompareShadowEntryAsync(
        TradingShadowEntryObservation observation,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync("/v1/shadow/entries", observation, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingShadowDecisionReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-shadow-receipt");
    }

    public async Task<TradingShadowSummary> GetShadowSummaryAsync(
        CancellationToken ct = default) =>
        await (_client ?? throw new InvalidOperationException("trading-core-client-disabled"))
            .GetFromJsonAsync<TradingShadowSummary>("/v1/shadow/summary", ct)
        ?? throw new InvalidOperationException("empty-trading-core-shadow-summary");

    public async Task<TradingCommandReceipt> SubmitEntryAsync(
        TradingEntryIntent intent,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync("/v1/commands/entries", intent, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-entry-receipt");
    }

    public async Task<TradingCommandReceipt> SubmitRecommendationAsync(
        TradingRecommendationObservation observation,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync(
            "/v1/recommendations", observation, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-recommendation-receipt");
    }

    public async Task<TradingCommandStatusView?> GetCommandAsync(
        string commandId,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.GetAsync(
            $"/v1/commands/{Uri.EscapeDataString(commandId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandStatusView>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-command-status");
    }

    public Task<TradingCommandStatusView?> GetLatestPositionCommandAsync(
        string positionId,
        CancellationToken ct = default) => GetOptionalCommandAsync(
            $"/v1/commands/positions/{Uri.EscapeDataString(positionId)}/latest", ct);

    public Task<TradingCommandStatusView?> GetLatestEntryCommandAsync(
        string sourceSignalId,
        CancellationToken ct = default) => GetOptionalCommandAsync(
            $"/v1/commands/entries/by-signal/{Uri.EscapeDataString(sourceSignalId)}/latest", ct);

    public async Task<TradingCommandReceipt> SubmitPositionAsync(
        TradingPositionCommand command,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync("/v1/commands/positions", command, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-position-receipt");
    }

    public async Task<TradingCommandReceipt> UpdatePositionStateAsync(
        TradingPositionPolicyStateUpdate update,
        CancellationToken ct = default)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.PostAsJsonAsync("/v1/positions/state", update, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandReceipt>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-position-state-receipt");
    }

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

    private async Task<TradingCommandStatusView?> GetOptionalCommandAsync(
        string path,
        CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException("trading-core-client-disabled");
        using var response = await client.GetAsync(path, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TradingCommandStatusView>(ct)
            ?? throw new InvalidOperationException("empty-trading-core-command-status");
    }

    private sealed record ProjectionReceipt(string SnapshotId, bool AlreadyApplied);
}

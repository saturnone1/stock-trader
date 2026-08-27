using System.Net.Http.Json;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.DataFeed;

public sealed class MarketDataServiceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    public MarketDataServiceClient(HttpClient http, IOptions<MarketDataTransportOptions> options)
    {
        _http = http;
        _ = options.Value;
    }

    public static HttpClientHandler CreateHandler(MarketDataTransportOptions options)
    {
        if (options.Mode == MarketDataTransportMode.Local)
            return new HttpClientHandler();
        var certificate = X509Certificate2.CreateFromPemFile(
            options.ClientCertificatePath,
            options.ClientCertificateKeyPath);
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(certificate);
        handler.ServerCertificateCustomValidationCallback = (_, server, _, errors) =>
            ValidateServer(options, server, errors);
        return handler;
    }

    public async Task<MarketDataRangeResponse> ReadRangeAsync(
        MarketDataRangeRequest request, CancellationToken ct) =>
        Validate(await PostAsync<MarketDataRangeRequest, MarketDataRangeResponse>(
            "/v1/bars/range", request, ct), request);

    public async Task<MarketDataBar?> ReadLatestAsync(
        MarketDataRangeRequest request, CancellationToken ct)
    {
        var response = Validate(await PostAsync<MarketDataRangeRequest, MarketDataRangeResponse>(
            "/v1/bars/latest", request, ct), request);
        return response.Bars.LastOrDefault();
    }

    public Task<MarketDataUpsertResponse> UpsertAsync(MarketDataUpsertRequest request, CancellationToken ct) =>
        PostAsync<MarketDataUpsertRequest, MarketDataUpsertResponse>("/v1/bars/upsert", request, ct);

    public async Task<MarketDataRangeResponse> HistoricalAsync(
        MarketDataProviderRequest request, CancellationToken ct) =>
        Validate(await PostAsync<MarketDataProviderRequest, MarketDataRangeResponse>(
            "/v1/provider/history", request, ct));

    public async Task<MarketDataRangeResponse> LatestAsync(
        MarketDataProviderRequest request, CancellationToken ct) =>
        Validate(await PostAsync<MarketDataProviderRequest, MarketDataRangeResponse>(
            "/v1/provider/latest", request, ct));

    public async Task<MarketDataRangeResponse> IntradayAsync(
        MarketDataIntradayRequest request, CancellationToken ct) =>
        Validate(await PostAsync<MarketDataIntradayRequest, MarketDataRangeResponse>(
            "/v1/provider/intraday", request, ct));

    public Task<MarketDataPriceResponse> PriceAsync(MarketDataPriceRequest request, CancellationToken ct) =>
        PostAsync<MarketDataPriceRequest, MarketDataPriceResponse>("/v1/provider/price", request, ct);

    public Task<MarketDataSubscriptionResponse> SetSubscriptionsAsync(
        MarketDataSubscriptionRequest request, CancellationToken ct) =>
        SendAsync<MarketDataSubscriptionRequest, MarketDataSubscriptionResponse>(
            HttpMethod.Put, "/v1/subscriptions", request, ct);

    public Task<MarketDataStoredSeriesResponse> SeriesAsync(CancellationToken ct) =>
        GetAsync<MarketDataStoredSeriesResponse>("/v1/series", authenticated: true, ct);

    public async Task<MarketDataServiceStatus> StatusAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/health/ready", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MarketDataServiceStatus>(Json, ct)
               ?? throw new InvalidDataException("Market Data status body was empty.");
    }

    private async Task<TResponse> GetAsync<TResponse>(
        string path, bool authenticated, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        _ = authenticated;
        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(Json, ct)
               ?? throw new InvalidDataException($"Market Data response for {path} was empty.");
    }

    private Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken ct) =>
        SendAsync<TRequest, TResponse>(HttpMethod.Post, path, request, ct);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string path, TRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(request, options: Json)
        };
        using var response = await _http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(Json, ct)
               ?? throw new InvalidDataException($"Market Data response for {path} was empty.");
    }

    private static MarketDataRangeResponse Validate(
        MarketDataRangeResponse response,
        MarketDataRangeRequest? expected = null)
    {
        var evidence = response.Evidence;
        if (evidence.ContractVersion != MarketDataContractVersions.Current)
            throw new InvalidDataException("Unsupported Market Data response contract.");
        var contentHash = MarketDataContractHash.Content(response.Bars);
        var evidenceId = MarketDataContractHash.Evidence(
            evidence.Provider, evidence.Symbol, evidence.TimeFrame, evidence.AdjustmentMode,
            evidence.CalendarVersion, evidence.Revision, contentHash);
        if (!string.Equals(contentHash, evidence.ContentHash, StringComparison.Ordinal) ||
            !string.Equals(evidenceId, evidence.EvidenceId, StringComparison.Ordinal))
            throw new InvalidDataException("Market Data evidence hash did not match its bars.");
        if (response.Bars.Any(bar =>
                !string.Equals(bar.Symbol, evidence.Symbol, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(bar.TimeFrame, evidence.TimeFrame, StringComparison.OrdinalIgnoreCase) ||
                MarketDataContractHash.Utc(bar.TimestampUtc) < evidence.RequestedFromUtc ||
                MarketDataContractHash.Utc(bar.TimestampUtc) > evidence.RequestedToUtc))
            throw new InvalidDataException("Market Data bars did not match their evidence identity or range.");
        if (expected is not null &&
            (!string.Equals(expected.Provider, evidence.Provider, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(expected.Symbol, evidence.Symbol, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(expected.TimeFrame, evidence.TimeFrame, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(expected.AdjustmentMode, evidence.AdjustmentMode, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(expected.Market, evidence.Market, StringComparison.Ordinal) ||
             !string.Equals(expected.CalendarVersion, evidence.CalendarVersion, StringComparison.Ordinal) ||
             expected.FromUtc != evidence.RequestedFromUtc ||
             expected.ToUtc != evidence.RequestedToUtc))
            throw new InvalidDataException("Market Data evidence identity did not match the request.");
        return response;
    }

    private static bool ValidateServer(
        MarketDataTransportOptions options,
        X509Certificate2? certificate,
        System.Net.Security.SslPolicyErrors errors)
    {
        if (certificate is null || errors.HasFlag(System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch))
            return false;
        if (!string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false),
                options.ServerCertificateCommonName, StringComparison.Ordinal))
            return false;
        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(options.ServerCertificateAuthorityPath);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return chain.Build(certificate);
    }
}

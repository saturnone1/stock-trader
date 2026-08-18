using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Application.Research;
using StockTrader.Configuration;
using StockTrader.Services.DataFeed;

namespace StockTrader.Services.Financial;

/// <summary>SEC 호출, 가격 보강, 저장을 조율하는 재무 스냅샷 수집기입니다.</summary>
public class SecFinancialSnapshotSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IFinancialCollectionStore _collectionStore;
    private readonly FinancialSnapshotImportService _importService;
    private readonly YahooFinanceDataFeedService _yahooFinance;
    private readonly FinancialDataPipelineSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecFinancialSnapshotSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _tickerMapLock = new(1, 1);

    private Dictionary<string, int>? _tickerToCik;
    private DateTime _tickerMapLoadedAtUtc = DateTime.MinValue;

    public SecFinancialSnapshotSyncService(
        HttpClient httpClient,
        IFinancialCollectionStore collectionStore,
        FinancialSnapshotImportService importService,
        YahooFinanceDataFeedService yahooFinance,
        IOptions<FinancialDataPipelineSettings> settings,
        TimeProvider timeProvider,
        ILogger<SecFinancialSnapshotSyncService> logger)
    {
        _httpClient = httpClient;
        _collectionStore = collectionStore;
        _importService = importService;
        _yahooFinance = yahooFinance;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FinancialVendorSyncStatus> GetStatusAsync(CancellationToken ct)
    {
        var latestSuccess = await _collectionStore.GetLatestCompletedAtAsync(
            SecFinancialSyncPolicy.ProviderName,
            requireImportedItems: true,
            ct);
        var configuredSymbols = SecFinancialSyncPolicy.ResolveSymbols(
            requestedSymbols: null,
            _settings.VendorSymbols,
            activeTickerSymbols: [],
            int.MaxValue);

        return new FinancialVendorSyncStatus
        {
            Enabled = _settings.VendorSyncEnabled,
            Provider = SecFinancialSyncPolicy.ProviderName,
            SyncIntervalHours = Math.Max(1, _settings.VendorSyncIntervalHours),
            SymbolLimit = Math.Max(1, _settings.VendorSymbolLimit),
            ConfiguredSymbolCount = configuredSymbols.Count,
            ConfiguredSymbols = configuredSymbols
                .Take(SecFinancialSyncPolicy.ConfiguredSymbolPreviewLimit)
                .ToList(),
            LatestSuccessAt = latestSuccess
        };
    }

    public Task<FinancialPipelineRunSummary> RunConfiguredSyncAsync(CancellationToken ct)
    {
        if (!_settings.VendorSyncEnabled)
        {
            return Task.FromResult(new FinancialPipelineRunSummary
            {
                Status = "Skipped",
                Message = "SEC vendor sync is disabled."
            });
        }

        return RunSyncAsync(null, ct, force: false);
    }

    public async Task<FinancialPipelineRunSummary> RunSyncAsync(
        IReadOnlyCollection<string>? requestedSymbols,
        CancellationToken ct,
        bool force)
    {
        if (!await _syncLock.WaitAsync(0, ct))
        {
            return new FinancialPipelineRunSummary
            {
                Status = "Skipped",
                Message = "A vendor sync is already running."
            };
        }

        long? runId = null;
        try
        {
            var explicitlyRequested = requestedSymbols is { Count: > 0 };
            var symbols = await ResolveSymbolsAsync(requestedSymbols, ct);
            if (symbols.Count == 0)
            {
                return new FinancialPipelineRunSummary
                {
                    Status = "Skipped",
                    Message = "No symbols available for SEC vendor sync."
                };
            }

            var intervalHours = Math.Max(1, _settings.VendorSyncIntervalHours);
            if (!force)
            {
                var latestCompleted = await _collectionStore.GetLatestCompletedAtAsync(
                    SecFinancialSyncPolicy.ProviderName,
                    requireImportedItems: false,
                    ct);
                if (SecFinancialSyncPolicy.IsWithinAutomaticInterval(
                    latestCompleted,
                    UtcNow,
                    intervalHours))
                {
                    return new FinancialPipelineRunSummary
                    {
                        Status = "Skipped",
                        Message = $"SEC vendor sync skipped because the last successful run is still within {intervalHours} hour(s)."
                    };
                }
            }

            var startedAt = UtcNow;
            runId = await _collectionStore.StartOrRestartRunAsync(
                SecFinancialSyncPolicy.ProviderName,
                SecFinancialSyncPolicy.BuildRunLabel(symbols, explicitlyRequested),
                SecFinancialSyncPolicy.BuildFingerprint(symbols, startedAt),
                startedAt,
                ct);
            var tickers = await _collectionStore.LoadTickersAsync(symbols, ct);
            var importItems = new List<FinancialSnapshotImportItem>();
            var skipped = 0;
            var failures = new List<string>();

            foreach (var symbol in symbols)
            {
                try
                {
                    tickers.TryGetValue(symbol, out var ticker);
                    var item = await BuildSnapshotAsync(symbol, ticker, ct);
                    if (item is null)
                        skipped++;
                    else
                        importItems.Add(item);
                }
                catch (Exception ex)
                {
                    skipped++;
                    failures.Add($"{symbol}: {ex.Message}");
                    _logger.LogWarning(ex, "SEC vendor sync failed for {Symbol}", symbol);
                }

                await Task.Delay(SecFinancialSyncPolicy.RequestDelay, ct);
            }

            var summary = importItems.Count > 0
                ? await _importService.UpsertAsync(importItems, ct)
                : new FinancialImportSummary();
            var totalSkipped = skipped + summary.SkippedCount;
            await _collectionStore.CompleteRunAsync(
                runId.Value,
                summary.ImportedCount,
                totalSkipped,
                failures.Count > 0 ? string.Join(" | ", failures.Take(3)) : null,
                UtcNow,
                ct);

            return new FinancialPipelineRunSummary
            {
                Status = "Completed",
                Message = $"SEC vendor sync processed {symbols.Count} symbol(s).",
                ImportedCount = summary.ImportedCount,
                SkippedCount = totalSkipped,
                ProcessedFiles = symbols.Count
            };
        }
        catch (Exception ex)
        {
            if (runId.HasValue)
                await _collectionStore.FailRunAsync(runId.Value, ex.Message, UtcNow, ct);

            _logger.LogError(ex, "SEC vendor sync failed");
            return new FinancialPipelineRunSummary
            {
                Status = "Failed",
                Message = ex.Message
            };
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ResolveSymbolsAsync(
        IReadOnlyCollection<string>? requestedSymbols,
        CancellationToken ct)
    {
        var resolved = SecFinancialSyncPolicy.ResolveSymbols(
            requestedSymbols,
            _settings.VendorSymbols,
            activeTickerSymbols: [],
            _settings.VendorSymbolLimit);
        if (resolved.Count > 0)
            return resolved;

        var activeTickers = await _collectionStore.LoadTopActiveTickersAsync(
            _settings.VendorSymbolLimit,
            ct);
        return SecFinancialSyncPolicy.ResolveSymbols(
            requestedSymbols,
            _settings.VendorSymbols,
            activeTickers.Select(ticker => ticker.Symbol).ToArray(),
            _settings.VendorSymbolLimit);
    }

    private async Task<FinancialSnapshotImportItem?> BuildSnapshotAsync(
        string symbol,
        ResearchTickerSnapshot? ticker,
        CancellationToken ct)
    {
        var tickerMap = await GetTickerMapAsync(ct);
        if (!tickerMap.TryGetValue(symbol, out var cik))
            return null;

        using var response = await _httpClient.GetAsync(
            $"/api/xbrl/companyfacts/CIK{cik:D10}.json",
            ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var facts = SecFinancialDocumentParser.Parse(document.RootElement);
        if (facts is null)
            return null;

        var currentPrice = facts.SharesOutstanding.HasValue
            ? await _yahooFinance.GetCurrentPriceAsync(symbol, ct)
            : 0m;
        return SecFinancialSnapshotFactory.Create(
            symbol,
            facts,
            ticker?.MarketCap,
            currentPrice,
            UtcNow.Date);
    }

    private async Task<Dictionary<string, int>> GetTickerMapAsync(CancellationToken ct)
    {
        if (HasFreshTickerMap())
            return _tickerToCik!;

        await _tickerMapLock.WaitAsync(ct);
        try
        {
            if (HasFreshTickerMap())
                return _tickerToCik!;

            using var response = await _httpClient.GetAsync("/files/company_tickers.json", ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, SecTickerMapEntry>>(
                stream,
                JsonOptions,
                ct) ?? [];
            _tickerToCik = payload.Values
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Ticker))
                .GroupBy(entry => SecFinancialSyncPolicy.NormalizeSymbol(entry.Ticker))
                .ToDictionary(
                    group => group.Key,
                    group => group.First().CikStr,
                    StringComparer.OrdinalIgnoreCase);
            _tickerMapLoadedAtUtc = UtcNow;
            return _tickerToCik;
        }
        finally
        {
            _tickerMapLock.Release();
        }
    }

    private bool HasFreshTickerMap() =>
        _tickerToCik is not null
        && _tickerMapLoadedAtUtc >= UtcNow - SecFinancialSyncPolicy.TickerMapCacheDuration;

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed class SecTickerMapEntry
    {
        public int CikStr { get; set; }
        public string Ticker { get; set; } = string.Empty;
    }
}

public class FinancialVendorSyncStatus
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int SyncIntervalHours { get; set; }
    public int SymbolLimit { get; set; }
    public int ConfiguredSymbolCount { get; set; }
    public List<string> ConfiguredSymbols { get; set; } = new();
    public DateTime? LatestSuccessAt { get; set; }
}

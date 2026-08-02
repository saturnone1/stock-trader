using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Api;
using StockTrader.BackgroundServices;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Services.DataFeed;

namespace StockTrader.Services.Financial;

public class SecFinancialSnapshotSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] RevenueConcepts =
    {
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "RevenueFromContractWithCustomerIncludingAssessedTax",
        "SalesRevenueNet"
    };

    private static readonly string[] OperatingIncomeConcepts =
    {
        "OperatingIncomeLoss"
    };

    private static readonly string[] NetIncomeConcepts =
    {
        "NetIncomeLoss",
        "ProfitLoss"
    };

    private static readonly string[] EquityConcepts =
    {
        "StockholdersEquity",
        "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest"
    };

    private static readonly string[] ShareConcepts =
    {
        "EntityCommonStockSharesOutstanding"
    };

    private readonly HttpClient _httpClient;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FinancialSnapshotImportService _importService;
    private readonly YahooFinanceDataFeedService _yahooFinance;
    private readonly FinancialDataPipelineSettings _settings;
    private readonly ILogger<SecFinancialSnapshotSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly SemaphoreSlim _tickerMapLock = new(1, 1);

    private Dictionary<string, int>? _tickerToCik;
    private DateTime _tickerMapLoadedAtUtc = DateTime.MinValue;

    public SecFinancialSnapshotSyncService(
        HttpClient httpClient,
        IDbContextFactory<AppDbContext> dbFactory,
        FinancialSnapshotImportService importService,
        YahooFinanceDataFeedService yahooFinance,
        IOptions<FinancialDataPipelineSettings> settings,
        ILogger<SecFinancialSnapshotSyncService> logger)
    {
        _httpClient = httpClient;
        _dbFactory = dbFactory;
        _importService = importService;
        _yahooFinance = yahooFinance;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<FinancialVendorSyncStatus> GetStatusAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var latestSuccess = await db.FinancialImportRuns
            .AsNoTracking()
            .Where(run => run.SourceType == "SEC" && run.Status == "Completed" && run.ImportedCount > 0)
            .OrderByDescending(run => run.CompletedAt)
            .FirstOrDefaultAsync(ct);

        var configuredSymbols = ParseSymbols(_settings.VendorSymbols).ToList();

        return new FinancialVendorSyncStatus
        {
            Enabled = _settings.VendorSyncEnabled,
            Provider = "SEC",
            SyncIntervalHours = Math.Max(1, _settings.VendorSyncIntervalHours),
            SymbolLimit = Math.Max(1, _settings.VendorSymbolLimit),
            ConfiguredSymbolCount = configuredSymbols.Count,
            ConfiguredSymbols = configuredSymbols.Take(20).ToList(),
            LatestSuccessAt = latestSuccess?.CompletedAt
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

        FinancialImportRun? run = null;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var symbols = await ResolveSymbolsAsync(db, requestedSymbols, ct);
            if (symbols.Count == 0)
            {
                return new FinancialPipelineRunSummary
                {
                    Status = "Skipped",
                    Message = "No symbols available for SEC vendor sync."
                };
            }

            if (!force)
            {
                var intervalHours = Math.Max(1, _settings.VendorSyncIntervalHours);
                var latestCompleted = await db.FinancialImportRuns
                    .AsNoTracking()
                    .Where(item => item.SourceType == "SEC" && item.Status == "Completed")
                    .OrderByDescending(item => item.CompletedAt)
                    .FirstOrDefaultAsync(ct);

                if (latestCompleted?.CompletedAt != null &&
                    latestCompleted.CompletedAt.Value >= DateTime.UtcNow.AddHours(-intervalHours))
                {
                    return new FinancialPipelineRunSummary
                    {
                        Status = "Skipped",
                        Message = $"SEC vendor sync skipped because the last successful run is still within {intervalHours} hour(s)."
                    };
                }
            }

            run = new FinancialImportRun
            {
                SourceType = "SEC",
                FilePath = BuildRunLabel(symbols, requestedSymbols),
                Fingerprint = BuildFingerprint(symbols),
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            db.FinancialImportRuns.Add(run);
            await db.SaveChangesAsync(ct);

            var tickers = await db.Tickers
                .AsNoTracking()
                .Where(ticker => symbols.Contains(ticker.Symbol))
                .ToDictionaryAsync(ticker => ticker.Symbol, ct);

            var importItems = new List<FinancialSnapshotImportDto>();
            var skipped = 0;
            var failures = new List<string>();

            foreach (var symbol in symbols)
            {
                try
                {
                    tickers.TryGetValue(symbol, out var ticker);
                    var item = await BuildSnapshotAsync(symbol, ticker, ct);
                    if (item == null)
                    {
                        skipped++;
                    }
                    else
                    {
                        importItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    failures.Add($"{symbol}: {ex.Message}");
                    _logger.LogWarning(ex, "SEC vendor sync failed for {Symbol}", symbol);
                }

                await Task.Delay(150, ct);
            }

            var summary = importItems.Count > 0
                ? await _importService.UpsertAsync(db, importItems, ct)
                : new FinancialImportSummary();

            run.Status = "Completed";
            run.ImportedCount = summary.ImportedCount;
            run.SkippedCount = skipped + summary.SkippedCount;
            run.ErrorMessage = failures.Count > 0 ? string.Join(" | ", failures.Take(3)) : null;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return new FinancialPipelineRunSummary
            {
                Status = "Completed",
                Message = $"SEC vendor sync processed {symbols.Count} symbol(s).",
                ImportedCount = summary.ImportedCount,
                SkippedCount = skipped + summary.SkippedCount,
                ProcessedFiles = symbols.Count
            };
        }
        catch (Exception ex)
        {
            if (run != null)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var trackedRun = await db.FinancialImportRuns.FirstOrDefaultAsync(item => item.Id == run.Id, ct);
                if (trackedRun != null)
                {
                    trackedRun.Status = "Failed";
                    trackedRun.ErrorMessage = ex.Message;
                    trackedRun.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }

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

    private async Task<List<string>> ResolveSymbolsAsync(
        AppDbContext db,
        IReadOnlyCollection<string>? requestedSymbols,
        CancellationToken ct)
    {
        if (requestedSymbols is { Count: > 0 })
            return requestedSymbols.Select(NormalizeSymbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var configured = ParseSymbols(_settings.VendorSymbols).ToList();
        if (configured.Count > 0)
            return configured.Take(Math.Max(1, _settings.VendorSymbolLimit)).ToList();

        return await db.Tickers
            .AsNoTracking()
            .Where(ticker => ticker.IsActive)
            .OrderByDescending(ticker => ticker.MarketCap)
            .ThenBy(ticker => ticker.Symbol)
            .Select(ticker => ticker.Symbol)
            .Take(Math.Max(1, _settings.VendorSymbolLimit))
            .ToListAsync(ct);
    }

    private async Task<FinancialSnapshotImportDto?> BuildSnapshotAsync(string symbol, Ticker? ticker, CancellationToken ct)
    {
        var tickerMap = await GetTickerMapAsync(ct);
        if (!tickerMap.TryGetValue(symbol, out var cik))
            return null;

        using var response = await _httpClient.GetAsync($"/api/xbrl/companyfacts/CIK{cik:D10}.json", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("facts", out var facts))
            return null;

        var revenue = ExtractAnnualPair(facts, "us-gaap", RevenueConcepts, "USD");
        var operatingIncome = ExtractAnnualPair(facts, "us-gaap", OperatingIncomeConcepts, "USD");
        var netIncome = ExtractAnnualPair(facts, "us-gaap", NetIncomeConcepts, "USD");
        var equity = ExtractLatestValue(facts, "us-gaap", EquityConcepts, "USD", annualOnly: true);
        var sharesOutstanding = ExtractLatestValue(facts, "dei", ShareConcepts, "shares", annualOnly: false);

        var asOfDate = revenue.AsOfDate
            ?? netIncome.AsOfDate
            ?? operatingIncome.AsOfDate
            ?? equity.AsOfDate
            ?? DateTime.UtcNow.Date;

        decimal? marketCap = ticker?.MarketCap;

        if (sharesOutstanding.Value.HasValue)
        {
            var currentPrice = await _yahooFinance.GetCurrentPriceAsync(symbol, ct);
            if (currentPrice > 0)
                marketCap = currentPrice * sharesOutstanding.Value.Value;
        }

        decimal? pbRatio = marketCap.HasValue && equity.Value.HasValue && equity.Value.Value > 0
            ? Math.Round(marketCap.Value / equity.Value.Value, 4)
            : null;

        decimal? peRatio = marketCap.HasValue && netIncome.Current.HasValue && netIncome.Current.Value > 0
            ? Math.Round(marketCap.Value / netIncome.Current.Value, 4)
            : null;

        decimal? roePercent = netIncome.Current.HasValue && equity.Value.HasValue && equity.Value.Value != 0
            ? Math.Round((netIncome.Current.Value / equity.Value.Value) * 100m, 4)
            : null;

        decimal? operatingMarginPercent = operatingIncome.Current.HasValue && revenue.Current.HasValue && revenue.Current.Value != 0
            ? Math.Round((operatingIncome.Current.Value / revenue.Current.Value) * 100m, 4)
            : null;

        if (revenue.Current == null &&
            operatingIncome.Current == null &&
            netIncome.Current == null &&
            peRatio == null &&
            pbRatio == null &&
            roePercent == null)
        {
            return null;
        }

        return new FinancialSnapshotImportDto
        {
            Symbol = symbol,
            AsOfDate = asOfDate,
            Source = "SEC",
            PeRatio = peRatio,
            PbRatio = pbRatio,
            RoePercent = roePercent,
            OperatingMarginPercent = operatingMarginPercent,
            RevenueCurrent = revenue.Current,
            RevenuePrevious = revenue.Previous,
            OperatingIncomeCurrent = operatingIncome.Current,
            OperatingIncomePrevious = operatingIncome.Previous,
            NetIncomeCurrent = netIncome.Current,
            NetIncomePrevious = netIncome.Previous,
            Notes = "External SEC sync"
        };
    }

    private async Task<Dictionary<string, int>> GetTickerMapAsync(CancellationToken ct)
    {
        if (_tickerToCik != null && _tickerMapLoadedAtUtc >= DateTime.UtcNow.AddHours(-12))
            return _tickerToCik;

        await _tickerMapLock.WaitAsync(ct);
        try
        {
            if (_tickerToCik != null && _tickerMapLoadedAtUtc >= DateTime.UtcNow.AddHours(-12))
                return _tickerToCik;

            using var response = await _httpClient.GetAsync("/files/company_tickers.json", ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, SecTickerMapEntry>>(stream, JsonOptions, ct)
                ?? new Dictionary<string, SecTickerMapEntry>();

            _tickerToCik = payload.Values
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Ticker))
                .GroupBy(entry => NormalizeSymbol(entry.Ticker))
                .ToDictionary(group => group.Key, group => group.First().CikStr, StringComparer.OrdinalIgnoreCase);
            _tickerMapLoadedAtUtc = DateTime.UtcNow;
            return _tickerToCik;
        }
        finally
        {
            _tickerMapLock.Release();
        }
    }

    private static string BuildRunLabel(IReadOnlyCollection<string> symbols, IReadOnlyCollection<string>? requestedSymbols)
    {
        if (requestedSymbols is { Count: > 0 })
            return $"SEC:{string.Join(',', symbols.Take(10))}{(symbols.Count > 10 ? $" (+{symbols.Count - 10})" : string.Empty)}";

        return $"SEC:auto:{symbols.Count}";
    }

    private static string BuildFingerprint(IReadOnlyCollection<string> symbols)
    {
        return $"SEC|{DateTime.UtcNow:yyyyMMddHHmmss}|{string.Join(',', symbols)}";
    }

    private static HashSet<string> ParseSymbols(string? raw)
    {
        return raw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSymbol(string value)
    {
        return value.Trim().ToUpperInvariant().Replace('.', '-');
    }

    private static FinancialMetricPair ExtractAnnualPair(
        JsonElement facts,
        string taxonomy,
        IEnumerable<string> concepts,
        string unitName)
    {
        foreach (var concept in concepts)
        {
            var entries = GetMetricEntries(facts, taxonomy, concept, unitName, annualOnly: true);
            if (entries.Count == 0)
                continue;

            var ordered = entries
                .GroupBy(entry => entry.End.Date)
                .Select(group => group.OrderByDescending(item => item.Filed ?? DateTime.MinValue).First())
                .OrderByDescending(entry => entry.End)
                .ToList();

            if (ordered.Count == 0)
                continue;

            return new FinancialMetricPair(
                ordered[0].Value,
                ordered.Count > 1 ? ordered[1].Value : null,
                ordered[0].End.Date);
        }

        return new FinancialMetricPair(null, null, null);
    }

    private static FinancialMetricValue ExtractLatestValue(
        JsonElement facts,
        string taxonomy,
        IEnumerable<string> concepts,
        string unitName,
        bool annualOnly)
    {
        foreach (var concept in concepts)
        {
            var entries = GetMetricEntries(facts, taxonomy, concept, unitName, annualOnly);
            var latest = entries
                .OrderByDescending(entry => entry.End)
                .ThenByDescending(entry => entry.Filed ?? DateTime.MinValue)
                .FirstOrDefault();

            if (latest != null)
                return new FinancialMetricValue(latest.Value, latest.End.Date);
        }

        return new FinancialMetricValue(null, null);
    }

    private static List<MetricEntry> GetMetricEntries(
        JsonElement facts,
        string taxonomy,
        string concept,
        string unitName,
        bool annualOnly)
    {
        if (!facts.TryGetProperty(taxonomy, out var taxonomyNode) ||
            !taxonomyNode.TryGetProperty(concept, out var conceptNode) ||
            !conceptNode.TryGetProperty("units", out var unitsNode) ||
            !unitsNode.TryGetProperty(unitName, out var valuesNode) ||
            valuesNode.ValueKind != JsonValueKind.Array)
        {
            return new List<MetricEntry>();
        }

        var result = new List<MetricEntry>();

        foreach (var item in valuesNode.EnumerateArray())
        {
            if (!TryReadDecimal(item, "val", out var value) ||
                !TryReadDate(item, "end", out var end))
                continue;

            item.TryGetProperty("fp", out var fpNode);
            item.TryGetProperty("form", out var formNode);
            item.TryGetProperty("filed", out var filedNode);
            var filed = filedNode.ValueKind == JsonValueKind.String ? TryParseDate(filedNode.GetString()) : null;
            var fp = fpNode.ValueKind == JsonValueKind.String ? fpNode.GetString() : null;
            var form = formNode.ValueKind == JsonValueKind.String ? formNode.GetString() : null;

            if (annualOnly && !IsAnnualEntry(fp, form))
                continue;

            result.Add(new MetricEntry(value, end, filed, fp, form));
        }

        return result;
    }

    private static bool IsAnnualEntry(string? fp, string? form)
    {
        if (string.Equals(fp, "FY", StringComparison.OrdinalIgnoreCase))
            return true;

        return form is "10-K" or "10-K/A" or "20-F" or "20-F/A" or "40-F" or "40-F/A";
    }

    private static bool TryReadDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var node))
            return false;

        if (node.ValueKind == JsonValueKind.Number)
            return node.TryGetDecimal(out value);

        if (node.ValueKind == JsonValueKind.String &&
            decimal.TryParse(node.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadDate(JsonElement element, string propertyName, out DateTime value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var node))
            return false;

        var raw = node.GetString();
        var parsed = TryParseDate(raw);
        if (!parsed.HasValue)
            return false;

        value = parsed.Value;
        return true;
    }

    private static DateTime? TryParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.Date
            : null;
    }

    private sealed record MetricEntry(decimal Value, DateTime End, DateTime? Filed, string? Fp, string? Form);

    private sealed record FinancialMetricPair(decimal? Current, decimal? Previous, DateTime? AsOfDate);

    private sealed record FinancialMetricValue(decimal? Value, DateTime? AsOfDate);

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

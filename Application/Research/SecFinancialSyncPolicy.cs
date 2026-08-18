using StockTrader.Domain.MarketData;

namespace StockTrader.Application.Research;

public static class SecFinancialSyncPolicy
{
    public const string ProviderName = "SEC";
    public const int ConfiguredSymbolPreviewLimit = 20;
    public static readonly TimeSpan TickerMapCacheDuration = TimeSpan.FromHours(12);
    public static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(150);

    public static IReadOnlyList<string> ResolveSymbols(
        IReadOnlyCollection<string>? requestedSymbols,
        string? configuredSymbols,
        IReadOnlyCollection<string> activeTickerSymbols,
        int configuredLimit)
    {
        var requested = NormalizeSymbols(requestedSymbols ?? []);
        if (requested.Count > 0)
            return requested;

        var configured = NormalizeSymbols(
            ResearchFilterPolicy.ParseCsv(configuredSymbols));
        if (configured.Count > 0)
            return configured.Take(Math.Max(1, configuredLimit)).ToArray();

        return NormalizeSymbols(activeTickerSymbols)
            .Take(Math.Max(1, configuredLimit))
            .ToArray();
    }

    public static bool IsWithinAutomaticInterval(
        DateTime? latestCompletedAt,
        DateTime nowUtc,
        int intervalHours) =>
        latestCompletedAt.HasValue
        && latestCompletedAt.Value >= nowUtc.AddHours(-Math.Max(1, intervalHours));

    public static string BuildRunLabel(
        IReadOnlyCollection<string> symbols,
        bool explicitlyRequested) =>
        explicitlyRequested
            ? $"SEC:{string.Join(',', symbols.Take(10))}{(symbols.Count > 10 ? $" (+{symbols.Count - 10})" : string.Empty)}"
            : $"SEC:auto:{symbols.Count}";

    public static string BuildFingerprint(
        IReadOnlyCollection<string> symbols,
        DateTime startedAt) =>
        $"SEC|{startedAt:yyyyMMddHHmmss}|{string.Join(',', symbols)}";

    public static string NormalizeSymbol(string value) =>
        MarketSymbolPolicy.Normalize(value).Replace('.', '-');

    private static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeSymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

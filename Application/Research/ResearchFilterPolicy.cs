namespace StockTrader.Application.Research;

public static class ResearchFilterPolicy
{
    public static IReadOnlySet<string> ParseCsv(string? raw) =>
        raw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public static class ResearchUniversePolicy
{
    public const int DefaultQueryLimit = 20;
    public const int MaximumUniverseQueryLimit = 100;
    public const int MaximumFinancialFactorQueryLimit = 5000;
    public const int SectorFacetLimit = 20;
    public const int IndustryFacetLimit = 30;
    public const int RecentImportRunLimit = 10;
}

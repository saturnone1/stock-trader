namespace StockTrader.Domain.Strategies;

public static class RuleOperatorCatalog
{
    private static readonly string[] Values = [">", "<", ">=", "<=", "crosses_above", "crosses_below"];

    public static IReadOnlyList<string> All { get; } = Values;

    public static bool Contains(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Values.Contains(value, StringComparer.OrdinalIgnoreCase);
}

using System.Text.RegularExpressions;

namespace StockTrader.Domain.MarketData;

/// <summary>시장 데이터·설정·실행 경로가 공유하는 종목 코드 정규화 계약입니다.</summary>
public static partial class MarketSymbolPolicy
{
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    public static IReadOnlyList<string> NormalizeMany(IEnumerable<string?> values) =>
        values
            .SelectMany(value => (value ?? string.Empty).Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries))
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsValid(string? value) =>
        SymbolPattern().IsMatch(Normalize(value));

    [GeneratedRegex("^[A-Z0-9][A-Z0-9.-]{0,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();
}

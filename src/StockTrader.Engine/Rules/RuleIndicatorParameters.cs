using StockTrader.Domain.Strategies;

namespace StockTrader.Engine.Rules;

public readonly record struct RuleIndicatorParameters(
    string Indicator,
    IReadOnlyDictionary<string, decimal> Values)
{
    internal int GetInt(string key, int fallback) =>
        Values.TryGetValue(key, out var value)
            ? (int)value
            : (int)IndicatorCatalog.ParameterDefault(Indicator, key, fallback);

    internal decimal GetDecimal(string key, decimal fallback) =>
        Values.TryGetValue(key, out var value)
            ? value
            : IndicatorCatalog.ParameterDefault(Indicator, key, fallback);
}

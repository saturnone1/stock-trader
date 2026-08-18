using System.Collections.Frozen;

namespace StockTrader.Application.Risk;

internal sealed record RiskStateGeneration(
    IReadOnlyDictionary<int, RiskStateSnapshot> Accounts,
    RiskStateSnapshot Portfolio,
    RiskStateSnapshot Fallback);

internal sealed class RiskStateStore
{
    private RiskStateGeneration _generation = EmptyGeneration();

    public RiskStateGeneration Snapshot() => Volatile.Read(ref _generation);

    public void Publish(RiskStateGeneration generation) =>
        Volatile.Write(ref _generation, generation);

    public static RiskStateSnapshot Empty(DateTime observedAt = default) => new(
        0m,
        0m,
        false,
        0,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        observedAt);

    private static RiskStateGeneration EmptyGeneration()
    {
        var empty = Empty();
        return new RiskStateGeneration(
            new Dictionary<int, RiskStateSnapshot>().ToFrozenDictionary(),
            empty,
            empty);
    }
}

using StockTrader.Application.Execution;

namespace StockTrader.Application.Signals;

/// <summary>
/// 한 번의 실시간 신호 평가가 참조할 영속 상태의 일관된 읽기 스냅샷입니다.
/// </summary>
public sealed class LiveSignalEvaluationSnapshot
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<StrategyCompletedTrade>>
        EmptyTrades = new Dictionary<string, IReadOnlyList<StrategyCompletedTrade>>(
            StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, int> EmptyCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> EmptySectors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static LiveSignalEvaluationSnapshot Empty { get; } = new(
        EmptyTrades,
        0,
        EmptyCounts,
        EmptySectors);

    public LiveSignalEvaluationSnapshot(
        IReadOnlyDictionary<string, IReadOnlyList<StrategyCompletedTrade>> completedTradesByStrategy,
        int openPositionCount,
        IReadOnlyDictionary<string, int> executedEntriesByStrategy,
        IReadOnlyDictionary<string, string> sectorBySymbol)
    {
        CompletedTradesByStrategy = completedTradesByStrategy;
        OpenPositionCount = openPositionCount;
        ExecutedEntriesByStrategy = executedEntriesByStrategy;
        SectorBySymbol = sectorBySymbol;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<StrategyCompletedTrade>> CompletedTradesByStrategy { get; }
    public int OpenPositionCount { get; }
    public IReadOnlyDictionary<string, int> ExecutedEntriesByStrategy { get; }
    public IReadOnlyDictionary<string, string> SectorBySymbol { get; }

    public IReadOnlyList<StrategyCompletedTrade> CompletedTradesFor(string? strategyName) =>
        !string.IsNullOrWhiteSpace(strategyName)
        && CompletedTradesByStrategy.TryGetValue(strategyName, out var trades)
            ? trades
            : [];

    public int ExecutedEntriesFor(string? strategyName) =>
        !string.IsNullOrWhiteSpace(strategyName)
        && ExecutedEntriesByStrategy.TryGetValue(strategyName, out var count)
            ? count
            : 0;

    public string? SectorFor(string symbol) =>
        SectorBySymbol.TryGetValue(symbol, out var sector) ? sector : null;
}

public interface ILiveSignalEvaluationStore
{
    Task<LiveSignalEvaluationSnapshot> LoadAsync(
        IReadOnlyCollection<string> strategyNames,
        IReadOnlyCollection<string> symbols,
        DateTime marketSessionStartUtc,
        CancellationToken ct = default);
}

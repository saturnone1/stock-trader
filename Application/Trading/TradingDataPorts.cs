using StockTrader.Models;

namespace StockTrader.Application.Trading;

public interface ITradeHistoryStore
{
    Task<List<TradeRecord>> GetTradesAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 1000,
        CancellationToken ct = default);

    Task<int> GetTradeCountAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    Task<List<TradeRecord>> GetRecentAsync(
        int limit = 5000,
        CancellationToken ct = default);
}

public interface IOpenPositionStore
{
    Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task SavePositionAsync(Position position, CancellationToken ct = default);
}

public interface ITradeRecommendationStore
{
    Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(
        int count = 20,
        CancellationToken ct = default);

    Task AddRecommendationAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default);
}

public interface IPatternSignalStore
{
    Task<List<PatternSignal>> GetActionableSignalsAsync(
        DateTime detectedFromInclusiveUtc,
        DateTime detectedThroughInclusiveUtc,
        CancellationToken ct = default);
    Task AddSignalsBatchAsync(
        IEnumerable<PatternSignal> signals,
        CancellationToken ct = default);
}

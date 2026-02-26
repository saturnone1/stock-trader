using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface ITradeRepository
{
    Task<List<TradeRecord>> GetTradesAsync(PatternType? patternType = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<List<TradeRecord>> GetRecentAsync(int limit = 5000, CancellationToken ct = default);
    Task AddTradeAsync(TradeRecord trade, CancellationToken ct = default);
    Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task<Position?> GetPositionAsync(long id, CancellationToken ct = default);
    Task SavePositionAsync(Position position, CancellationToken ct = default);
    Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(int count = 20,
        CancellationToken ct = default);
    Task AddRecommendationAsync(TradeRecommendation recommendation, CancellationToken ct = default);
    Task<List<PatternSignal>> GetActiveSignalsAsync(CancellationToken ct = default);
    Task AddSignalAsync(PatternSignal signal, CancellationToken ct = default);
    Task DeactivateSignalAsync(long signalId, CancellationToken ct = default);
}

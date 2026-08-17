using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface ITradeRepository
{
    /// <summary>
    /// 거래 내역을 조회한다. take=0이면 최대 1000건으로 제한된다.
    /// 전체 조회가 필요한 경우 take에 int.MaxValue를 명시적으로 전달한다.
    /// </summary>
    Task<List<TradeRecord>> GetTradesAsync(PatternType? patternType = null,
        DateTime? from = null, DateTime? to = null,
        int skip = 0, int take = 1000, CancellationToken ct = default);
    Task<int> GetTradeCountAsync(PatternType? patternType = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<List<TradeRecord>> GetRecentAsync(int limit = 5000, CancellationToken ct = default);
    Task AddTradeAsync(TradeRecord trade, CancellationToken ct = default);
    Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task<Position?> GetPositionAsync(long id, CancellationToken ct = default);
    Task SavePositionAsync(Position position, CancellationToken ct = default);
    Task<bool> TryClaimPositionExitAsync(long positionId, DateTime requestedAt, string reason,
        CancellationToken ct = default);
    Task<bool> SetPositionExitOrderIdAsync(long positionId, DateTime requestedAt, string? orderId,
        CancellationToken ct = default);
    Task<bool> ReleasePositionExitClaimAsync(long positionId, DateTime requestedAt,
        CancellationToken ct = default);
    Task<bool> TryCompletePositionExitAsync(Position position, TradeRecord trade,
        CancellationToken ct = default);
    Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(int count = 20,
        CancellationToken ct = default);
    Task AddRecommendationAsync(TradeRecommendation recommendation, CancellationToken ct = default);
    Task UpdateRecommendationAsync(TradeRecommendation recommendation, CancellationToken ct = default);
    Task<List<PatternSignal>> GetActiveSignalsAsync(CancellationToken ct = default);
    Task AddSignalAsync(PatternSignal signal, CancellationToken ct = default);
    Task AddSignalsBatchAsync(IEnumerable<PatternSignal> signals, CancellationToken ct = default);
    Task DeactivateSignalAsync(long signalId, CancellationToken ct = default);
}

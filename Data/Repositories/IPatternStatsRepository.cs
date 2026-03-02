using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface IPatternStatsRepository
{
    Task<PatternStats?> GetAsync(PatternType patternType, string? symbol = null,
        CancellationToken ct = default);
    Task<List<PatternStats>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(PatternStats stats, CancellationToken ct = default);

    /// <summary>
    /// statsList를 기준으로 DB를 동기화한다. statsList에 없는 기존 행은 삭제된다.
    /// </summary>
    Task SaveBatchAsync(IEnumerable<PatternStats> statsList, CancellationToken ct = default);
}

using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public interface IPatternStatsRepository
{
    Task<PatternStats?> GetAsync(PatternType patternType, string? symbol = null,
        CancellationToken ct = default);
    Task<List<PatternStats>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(PatternStats stats, CancellationToken ct = default);
}

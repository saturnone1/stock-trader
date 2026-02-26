using StockTrader.Models;

namespace StockTrader.Services.Statistics;

public interface IStatisticsService
{
    Task<PatternStats?> GetStatsAsync(PatternType pattern, string? symbol = null,
        CancellationToken ct = default);
    Task<List<PatternStats>> GetAllStatsAsync(CancellationToken ct = default);
    Task<PatternStats> ComputeStatsAsync(PatternType pattern, List<TradeRecord> trades,
        CancellationToken ct = default);
    Task RefreshAllStatsAsync(CancellationToken ct = default);
}

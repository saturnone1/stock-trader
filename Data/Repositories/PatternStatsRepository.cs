using Microsoft.EntityFrameworkCore;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public class PatternStatsRepository : IPatternStatsRepository
{
    private readonly AppDbContext _db;

    public PatternStatsRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PatternStats?> GetAsync(PatternType patternType, string? symbol = null,
        CancellationToken ct = default)
    {
        return await _db.PatternStats
            .FirstOrDefaultAsync(s => s.PatternType == patternType && s.Symbol == symbol, ct);
    }

    public async Task<List<PatternStats>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.PatternStats
            .OrderBy(s => s.PatternType)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(PatternStats stats, CancellationToken ct = default)
    {
        var existing = await _db.PatternStats
            .FirstOrDefaultAsync(s => s.PatternType == stats.PatternType
                && s.Symbol == stats.Symbol, ct);

        if (existing != null)
        {
            existing.SampleSize = stats.SampleSize;
            existing.WinRate = stats.WinRate;
            existing.AvgWinPercent = stats.AvgWinPercent;
            existing.AvgLossPercent = stats.AvgLossPercent;
            existing.MaxDrawdownPercent = stats.MaxDrawdownPercent;
            existing.LastUpdated = DateTime.UtcNow;
        }
        else
        {
            stats.LastUpdated = DateTime.UtcNow;
            _db.PatternStats.Add(stats);
        }
        await _db.SaveChangesAsync(ct);
    }
}

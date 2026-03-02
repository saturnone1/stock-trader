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

    public async Task SaveBatchAsync(IEnumerable<PatternStats> statsList, CancellationToken ct = default)
    {
        // Load all existing rows in a single query, keyed by (PatternType, Symbol)
        var existing = await _db.PatternStats.ToListAsync(ct);
        var lookup = existing.ToDictionary(s => (s.PatternType, s.Symbol));

        var incoming = statsList.ToList();

        var now = DateTime.UtcNow;
        foreach (var stats in incoming)
        {
            if (lookup.TryGetValue((stats.PatternType, stats.Symbol), out var row))
            {
                row.SampleSize = stats.SampleSize;
                row.WinRate = stats.WinRate;
                row.AvgWinPercent = stats.AvgWinPercent;
                row.AvgLossPercent = stats.AvgLossPercent;
                row.MaxDrawdownPercent = stats.MaxDrawdownPercent;
                row.LastUpdated = now;
            }
            else
            {
                stats.LastUpdated = now;
                _db.PatternStats.Add(stats);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteStaleAsync(ISet<(PatternType PatternType, string? Symbol)> activeKeys,
        CancellationToken ct = default)
    {
        var existing = await _db.PatternStats.ToListAsync(ct);
        var stale = existing
            .Where(s => !activeKeys.Contains((s.PatternType, s.Symbol)))
            .ToList();

        if (stale.Count > 0)
        {
            _db.PatternStats.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }
    }
}

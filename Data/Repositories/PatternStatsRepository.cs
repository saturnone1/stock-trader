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
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PatternType == patternType && s.Symbol == symbol, ct);
    }

    public async Task<List<PatternStats>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.PatternStats
            .AsNoTracking()
            .OrderBy(s => s.PatternType)
            .ToListAsync(ct);
    }

    public async Task SaveBatchAsync(IEnumerable<PatternStats> statsList, CancellationToken ct = default)
    {
        var incoming = statsList.ToList();

        // Only load rows whose PatternType appears in the incoming batch — avoids full table scan
        var incomingTypes = incoming.Select(s => s.PatternType).Distinct().ToList();
        var existing = await _db.PatternStats
            .Where(s => incomingTypes.Contains(s.PatternType))
            .ToListAsync(ct);
        var lookup = existing.ToDictionary(s => (s.PatternType, s.Symbol));

        foreach (var stats in incoming)
        {
            if (lookup.TryGetValue((stats.PatternType, stats.Symbol), out var row))
            {
                row.SampleSize = stats.SampleSize;
                row.WinRate = stats.WinRate;
                row.AvgWinPercent = stats.AvgWinPercent;
                row.AvgLossPercent = stats.AvgLossPercent;
                row.MaxDrawdownPercent = stats.MaxDrawdownPercent;
                row.LastUpdated = stats.LastUpdated;
            }
            else
            {
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

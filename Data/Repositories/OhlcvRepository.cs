using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

public class OhlcvRepository : IOhlcvRepository
{
    private readonly AppDbContext _db;

    public OhlcvRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<OhlcvBar>> GetBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame
                && b.Timestamp >= from && b.Timestamp <= to)
            .OrderBy(b => b.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame)
            .OrderByDescending(b => b.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddBarsAsync(IEnumerable<OhlcvBar> bars, CancellationToken ct = default)
    {
        var barList = bars.ToList();
        if (barList.Count == 0) return;

        // Bulk check: load all existing keys in one query instead of N+1
        var symbols = barList.Select(b => b.Symbol).Distinct().ToList();
        var timeFrames = barList.Select(b => b.TimeFrame).Distinct().ToList();
        var minTs = barList.Min(b => b.Timestamp);
        var maxTs = barList.Max(b => b.Timestamp);

        var existingKeys = await _db.OhlcvBars
            .Where(b => symbols.Contains(b.Symbol)
                && timeFrames.Contains(b.TimeFrame)
                && b.Timestamp >= minTs && b.Timestamp <= maxTs)
            .Select(b => new { b.Symbol, b.TimeFrame, b.Timestamp })
            .ToListAsync(ct);

        var existingSet = new HashSet<(string, TimeFrame, DateTime)>(
            existingKeys.Select(k => (k.Symbol, k.TimeFrame, k.Timestamp)));

        foreach (var bar in barList)
        {
            if (!existingSet.Contains((bar.Symbol, bar.TimeFrame, bar.Timestamp)))
                _db.OhlcvBars.Add(bar);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DateTime?> GetLastTimestampAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default)
    {
        return await _db.OhlcvBars
            .Where(b => b.Symbol == symbol && b.TimeFrame == timeFrame)
            .MaxAsync(b => (DateTime?)b.Timestamp, ct);
    }
}

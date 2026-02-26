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
        foreach (var bar in bars)
        {
            var exists = await _db.OhlcvBars.AnyAsync(b =>
                b.Symbol == bar.Symbol
                && b.TimeFrame == bar.TimeFrame
                && b.Timestamp == bar.Timestamp, ct);

            if (!exists)
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

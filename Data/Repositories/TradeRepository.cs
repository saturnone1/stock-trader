using Microsoft.EntityFrameworkCore;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly AppDbContext _db;

    public TradeRepository(AppDbContext db)
    {
        _db = db;
    }

    // Default take=1000: prevents unbounded full-table scans on large datasets.
    // Callers that genuinely need all records must pass int.MaxValue explicitly.
    public async Task<List<TradeRecord>> GetTradesAsync(PatternType? patternType = null,
        DateTime? from = null, DateTime? to = null,
        int skip = 0, int take = 1000, CancellationToken ct = default)
    {
        var query = BuildTradeQuery(patternType, from, to);

        if (skip > 0) query = query.Skip(skip);
        // take=0 is treated as "use the default limit" to match legacy callers
        // that relied on take=0 meaning "no limit". Those callers now get the
        // 1000-row safety cap instead of an unbounded scan.
        query = query.Take(take > 0 ? take : 1000);

        return await query.ToListAsync(ct);
    }

    public async Task<int> GetTradeCountAsync(PatternType? patternType = null,
        DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _db.TradeRecords.AsNoTracking();

        if (patternType.HasValue)
            query = query.Where(t => t.PatternType == patternType.Value);
        if (from.HasValue)
            query = query.Where(t => t.EntryTime >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ExitTime <= to.Value);

        return await query.CountAsync(ct);
    }

    private IQueryable<TradeRecord> BuildTradeQuery(PatternType? patternType, DateTime? from, DateTime? to)
    {
        var query = _db.TradeRecords.AsNoTracking();

        if (patternType.HasValue)
            query = query.Where(t => t.PatternType == patternType.Value);
        if (from.HasValue)
            query = query.Where(t => t.EntryTime >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.ExitTime <= to.Value);

        return query.OrderByDescending(t => t.EntryTime);
    }

    public async Task<List<TradeRecord>> GetRecentAsync(int limit = 5000, CancellationToken ct = default)
    {
        return await _db.TradeRecords
            .AsNoTracking()
            .OrderByDescending(t => t.EntryTime)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddTradeAsync(TradeRecord trade, CancellationToken ct = default)
    {
        _db.TradeRecords.Add(trade);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        return await _db.Positions
            .AsNoTracking()
            .Where(p => p.ClosedAt == null)
            .OrderByDescending(p => p.OpenedAt)
            .ToListAsync(ct);
    }

    public async Task<Position?> GetPositionAsync(long id, CancellationToken ct = default)
    {
        return await _db.Positions.FindAsync(new object[] { id }, ct);
    }

    public async Task SavePositionAsync(Position position, CancellationToken ct = default)
    {
        if (position.Id == 0)
            _db.Positions.Add(position);
        else
            _db.Positions.Update(position);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(int count = 20,
        CancellationToken ct = default)
    {
        return await _db.TradeRecommendations
            .AsNoTracking()
            .OrderByDescending(r => r.GeneratedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task AddRecommendationAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _db.TradeRecommendations.Add(recommendation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<PatternSignal>> GetActiveSignalsAsync(CancellationToken ct = default)
    {
        return await _db.PatternSignals
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.DetectedAt)
            .ToListAsync(ct);
    }

    public async Task AddSignalAsync(PatternSignal signal, CancellationToken ct = default)
    {
        _db.PatternSignals.Add(signal);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddSignalsBatchAsync(IEnumerable<PatternSignal> signals, CancellationToken ct = default)
    {
        // AddRange stages all entities; single SaveChangesAsync writes them in one transaction
        _db.PatternSignals.AddRange(signals);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeactivateSignalAsync(long signalId, CancellationToken ct = default)
    {
        var signal = await _db.PatternSignals.FindAsync(new object[] { signalId }, ct);
        if (signal != null)
        {
            signal.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }
}

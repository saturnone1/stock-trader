using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    private const string OpenPositionsCacheKey = "TradeRepo:OpenPositions";
    private const string ActiveSignalsCacheKey = "TradeRepo:ActiveSignals";
    private const string RecentRecsCacheKey = "TradeRepo:RecentRecs";
    private static readonly TimeSpan PositionCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignalCacheTtl = TimeSpan.FromSeconds(60);

    public TradeRepository(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
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
        if (_cache.TryGetValue(OpenPositionsCacheKey, out List<Position>? cached) && cached != null)
            return cached;

        var positions = await _db.Positions
            .AsNoTracking()
            .Where(p => p.ClosedAt == null)
            .OrderByDescending(p => p.OpenedAt)
            .ToListAsync(ct);

        _cache.Set(OpenPositionsCacheKey, positions, PositionCacheTtl);
        return positions;
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
        _cache.Remove(OpenPositionsCacheKey);
    }

    public async Task<bool> TryClaimPositionExitAsync(
        PositionExitClaim claim,
        CancellationToken ct = default)
    {
        if (claim.PositionId <= 0
            || claim.ExpectedPositionQuantity <= 0
            || claim.Quantity <= 0
            || claim.Quantity > claim.ExpectedPositionQuantity
            || string.IsNullOrWhiteSpace(claim.Reason))
            return false;

        var updated = await _db.Positions
            .Where(position => position.Id == claim.PositionId
                && position.ClosedAt == null
                && position.ExitRequestedAt == null
                && position.Quantity == claim.ExpectedPositionQuantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExitRequestedAt, claim.RequestedAt)
                .SetProperty(position => position.ExitRequestReason, claim.Reason)
                .SetProperty(position => position.ExitRequestQuantity, claim.Quantity)
                .SetProperty(position => position.ExitRequestMarksPartialProfit, claim.MarksPartialProfit)
                .SetProperty(position => position.ExitOrderId, (string?)null), ct);
        _cache.Remove(OpenPositionsCacheKey);
        return updated == 1;
    }

    public async Task<bool> SetPositionExitOrderIdAsync(
        long positionId,
        DateTime requestedAt,
        string? orderId,
        CancellationToken ct = default)
    {
        var updated = await _db.Positions
            .Where(position => position.Id == positionId
                && position.ClosedAt == null
                && position.ExitRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExitOrderId, orderId), ct);
        _cache.Remove(OpenPositionsCacheKey);
        return updated == 1;
    }

    public async Task<bool> ReleasePositionExitClaimAsync(
        long positionId,
        DateTime requestedAt,
        CancellationToken ct = default)
    {
        var updated = await _db.Positions
            .Where(position => position.Id == positionId
                && position.ClosedAt == null
                && position.ExitRequestedAt == requestedAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(position => position.ExitRequestedAt, (DateTime?)null)
                .SetProperty(position => position.ExitRequestReason, (string?)null)
                .SetProperty(position => position.ExitRequestQuantity, (int?)null)
                .SetProperty(position => position.ExitRequestMarksPartialProfit, false)
                .SetProperty(position => position.ExitOrderId, (string?)null), ct);
        _cache.Remove(OpenPositionsCacheKey);
        return updated == 1;
    }

    public async Task<bool> TryApplyPositionExitFillAsync(
        PositionExitFill fill,
        TradeRecord trade,
        CancellationToken ct = default)
    {
        if (fill.PositionId <= 0
            || fill.ExpectedPositionQuantity <= 0
            || fill.FilledQuantity <= 0
            || fill.FilledQuantity > fill.ExpectedPositionQuantity
            || fill.FillPrice <= 0)
            return false;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var query = _db.Positions
                .Where(stored => stored.Id == fill.PositionId
                    && stored.ClosedAt == null
                    && stored.ExitRequestedAt == fill.RequestedAt
                    && stored.ExitRequestQuantity == fill.FilledQuantity
                    && stored.Quantity == fill.ExpectedPositionQuantity);

            var isFullExit = fill.FilledQuantity == fill.ExpectedPositionQuantity;
            var updated = isFullExit
                ? await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(stored => stored.ClosedAt, fill.FilledAt)
                    .SetProperty(stored => stored.ExitPrice, fill.FillPrice)
                    .SetProperty(stored => stored.ExitOrderId, fill.OrderId), ct)
                : await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(stored => stored.Quantity,
                        fill.ExpectedPositionQuantity - fill.FilledQuantity)
                    .SetProperty(stored => stored.CurrentPrice, fill.FillPrice)
                    .SetProperty(stored => stored.PartialProfitTaken,
                        stored => stored.PartialProfitTaken || fill.MarksPartialProfit)
                    .SetProperty(stored => stored.ExitRequestedAt, (DateTime?)null)
                    .SetProperty(stored => stored.ExitRequestReason, (string?)null)
                    .SetProperty(stored => stored.ExitRequestQuantity, (int?)null)
                    .SetProperty(stored => stored.ExitRequestMarksPartialProfit, false)
                    .SetProperty(stored => stored.ExitOrderId, (string?)null), ct);
            if (updated != 1)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            _db.TradeRecords.Add(trade);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            _cache.Remove(OpenPositionsCacheKey);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.Entry(trade).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(int count = 20,
        CancellationToken ct = default)
    {
        var cacheKey = $"{RecentRecsCacheKey}:{count}";
        if (_cache.TryGetValue(cacheKey, out List<TradeRecommendation>? cached) && cached != null)
            return cached;

        var recs = await _db.TradeRecommendations
            .AsNoTracking()
            .OrderByDescending(r => r.GeneratedAt)
            .Take(count)
            .ToListAsync(ct);

        _cache.Set(cacheKey, recs, SignalCacheTtl);
        return recs;
    }

    public async Task AddRecommendationAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _db.TradeRecommendations.Add(recommendation);
        await _db.SaveChangesAsync(ct);
        InvalidateRecsCache();
    }

    public async Task UpdateRecommendationAsync(TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        _db.TradeRecommendations.Update(recommendation);
        await _db.SaveChangesAsync(ct);
        InvalidateRecsCache();
    }

    // count 파라미터가 있는 캐시 키는 정확한 값 제거가 불가능하므로
    // 공통 프리픽스 기반 무효화 대신 TTL에 의존 (60초 이내 자연 만료)
    // 단, 가장 많이 사용되는 주요 count 값은 명시적으로 제거한다.
    private void InvalidateRecsCache()
    {
        _cache.Remove($"{RecentRecsCacheKey}:20");
        _cache.Remove($"{RecentRecsCacheKey}:50");
        _cache.Remove($"{RecentRecsCacheKey}:100");
        _cache.Remove($"{RecentRecsCacheKey}:200");
    }

    public async Task<List<PatternSignal>> GetActiveSignalsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(ActiveSignalsCacheKey, out List<PatternSignal>? cached) && cached != null)
            return cached;

        var signals = await _db.PatternSignals
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.DetectedAt)
            .ToListAsync(ct);

        _cache.Set(ActiveSignalsCacheKey, signals, SignalCacheTtl);
        return signals;
    }

    public async Task AddSignalAsync(PatternSignal signal, CancellationToken ct = default)
    {
        _db.PatternSignals.Add(signal);
        await _db.SaveChangesAsync(ct);
        _cache.Remove(ActiveSignalsCacheKey);
    }

    public async Task AddSignalsBatchAsync(IEnumerable<PatternSignal> signals, CancellationToken ct = default)
    {
        // AddRange stages all entities; single SaveChangesAsync writes them in one transaction
        _db.PatternSignals.AddRange(signals);
        await _db.SaveChangesAsync(ct);
        _cache.Remove(ActiveSignalsCacheKey);
    }

    public async Task DeactivateSignalAsync(long signalId, CancellationToken ct = default)
    {
        var signal = await _db.PatternSignals.FindAsync(new object[] { signalId }, ct);
        if (signal != null)
        {
            signal.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _cache.Remove(ActiveSignalsCacheKey);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

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
        if (_cache.TryGetValue(TradeReadCache.OpenPositions, out List<Position>? cached) && cached != null)
            return cached;

        var positions = await _db.Positions
            .AsNoTracking()
            .Include(position => position.ScalingExecutions)
            .Where(p => p.ClosedAt == null)
            .OrderByDescending(p => p.OpenedAt)
            .ToListAsync(ct);

        _cache.Set(TradeReadCache.OpenPositions, positions, PositionCacheTtl);
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
        _cache.Remove(TradeReadCache.OpenPositions);
    }

    public async Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(int count = 20,
        CancellationToken ct = default)
    {
        var cacheKey = TradeReadCache.RecentRecommendations(count);
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
        if (recommendation.SourceSignalId.HasValue)
        {
            var existing = await _db.TradeRecommendations.AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.SourceSignalId == recommendation.SourceSignalId,
                    ct);
            if (existing is not null)
            {
                CopyPersistedEntryIdentity(existing, recommendation);
                return;
            }
        }

        _db.TradeRecommendations.Add(recommendation);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (recommendation.SourceSignalId.HasValue)
        {
            _db.Entry(recommendation).State = EntityState.Detached;
            var existing = await _db.TradeRecommendations.AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.SourceSignalId == recommendation.SourceSignalId,
                    ct);
            if (existing is null)
                throw;
            CopyPersistedEntryIdentity(existing, recommendation);
        }
        InvalidateRecsCache();
    }

    private static void CopyPersistedEntryIdentity(
        TradeRecommendation stored,
        TradeRecommendation target)
    {
        target.Id = stored.Id;
        target.SourceSignalId = stored.SourceSignalId;
        target.Symbol = stored.Symbol;
        target.PatternType = stored.PatternType;
        target.CustomPatternName = stored.CustomPatternName;
        target.GeneratedAt = stored.GeneratedAt;
        target.EntryPrice = stored.EntryPrice;
        target.StopLossPrice = stored.StopLossPrice;
        target.TargetPrice = stored.TargetPrice;
        target.PositionSize = stored.PositionSize;
        target.ShareQuantity = stored.ShareQuantity;
        target.Expectancy = stored.Expectancy;
        target.WasExecuted = stored.WasExecuted;
        target.EntryRequestedAt = stored.EntryRequestedAt;
        target.EntryAccountId = stored.EntryAccountId;
        target.EntryOrderId = stored.EntryOrderId;
        target.EntryExecutionNote = stored.EntryExecutionNote;
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
        TradeReadCache.InvalidateRecommendations(_cache);
    }

    public async Task<List<PatternSignal>> GetActiveSignalsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(TradeReadCache.ActiveSignals, out List<PatternSignal>? cached) && cached != null)
            return cached;

        var signals = await _db.PatternSignals
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.DetectedAt)
            .ToListAsync(ct);

        _cache.Set(TradeReadCache.ActiveSignals, signals, SignalCacheTtl);
        return signals;
    }

    public async Task AddSignalAsync(PatternSignal signal, CancellationToken ct = default)
    {
        await AddSignalsBatchAsync([signal], ct);
    }

    public async Task AddSignalsBatchAsync(IEnumerable<PatternSignal> signals, CancellationToken ct = default)
    {
        var candidates = signals.ToList();
        if (candidates.Count == 0)
            return;
        if (candidates.Any(signal => signal.SignalBarAt is null))
            throw new InvalidOperationException("Persisted pattern signals require a signal bar timestamp.");

        var patternTypes = candidates.Select(signal => signal.PatternType).Distinct().ToList();
        var barTimes = candidates.Select(signal => signal.SignalBarAt).Distinct().ToList();
        var existingSignals = await _db.PatternSignals
            .AsNoTracking()
            .Where(signal => patternTypes.Contains(signal.PatternType)
                && barTimes.Contains(signal.SignalBarAt))
            .Select(signal => new
            {
                signal.Id,
                signal.Symbol,
                signal.PatternType,
                signal.CustomPatternName,
                signal.SignalBarAt
            })
            .ToListAsync(ct);
        var persistedIds = existingSignals
            .ToDictionary(signal => SignalIdentity(
                signal.Symbol,
                signal.PatternType,
                signal.CustomPatternName,
                signal.SignalBarAt!.Value),
                signal => signal.Id,
                StringComparer.Ordinal);
        var identities = persistedIds.Keys.ToHashSet(StringComparer.Ordinal);
        var newSignals = candidates
            .Where(signal => identities.Add(SignalIdentity(
                signal.Symbol,
                signal.PatternType,
                signal.CustomPatternName,
                signal.SignalBarAt!.Value)))
            .ToList();
        if (newSignals.Count > 0)
        {
            // AddRange stages all new identities; single SaveChangesAsync writes them in one transaction.
            _db.PatternSignals.AddRange(newSignals);
            await _db.SaveChangesAsync(ct);
            foreach (var signal in newSignals)
                persistedIds[SignalIdentity(signal)] = signal.Id;
            _cache.Remove(TradeReadCache.ActiveSignals);
        }

        // 재시작 후 이미 저장된 동일 시그널도 추천 멱등 키를 잃지 않도록 ID를 되돌려 준다.
        foreach (var signal in candidates.Where(signal => signal.Id <= 0))
        {
            if (persistedIds.TryGetValue(SignalIdentity(signal), out var id))
                signal.Id = id;
        }
    }

    private static string SignalIdentity(
        string symbol,
        PatternType patternType,
        string? customPatternName,
        DateTime signalBarAt) => string.Join(
            '\u001f',
            symbol.Trim().ToUpperInvariant(),
            (int)patternType,
            customPatternName?.Trim().ToUpperInvariant() ?? string.Empty,
            signalBarAt.Ticks);

    private static string SignalIdentity(PatternSignal signal) => SignalIdentity(
        signal.Symbol,
        signal.PatternType,
        signal.CustomPatternName,
        signal.SignalBarAt!.Value);

    public async Task DeactivateSignalAsync(long signalId, CancellationToken ct = default)
    {
        var signal = await _db.PatternSignals.FindAsync(new object[] { signalId }, ct);
        if (signal != null)
        {
            signal.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _cache.Remove(TradeReadCache.ActiveSignals);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Trading;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class TradeRecommendationStore(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache)
    : ITradeRecommendationStore
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(
        int count = 20,
        CancellationToken ct = default)
    {
        var cacheKey = TradeReadCache.RecentRecommendations(count);
        if (cache.TryGetValue(cacheKey, out List<TradeRecommendation>? cached)
            && cached is not null)
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recommendations = await db.TradeRecommendations
            .AsNoTracking()
            .Where(recommendation => !recommendation.IsSuperseded)
            .OrderByDescending(recommendation => recommendation.GeneratedAt)
            .Take(count)
            .ToListAsync(ct);
        cache.Set(cacheKey, recommendations, CacheTtl);
        return recommendations;
    }

    public async Task AddRecommendationAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (recommendation.SourceSignalId.HasValue)
        {
            var existing = await FindBySourceSignalAsync(db, recommendation.SourceSignalId, ct);
            if (existing is not null)
            {
                CopyPersistedIdentity(existing, recommendation);
                return;
            }
        }

        db.TradeRecommendations.Add(recommendation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (recommendation.SourceSignalId.HasValue)
        {
            db.Entry(recommendation).State = EntityState.Detached;
            var existing = await FindBySourceSignalAsync(db, recommendation.SourceSignalId, ct);
            if (existing is null)
                throw;
            CopyPersistedIdentity(existing, recommendation);
        }
        TradeReadCache.InvalidateRecommendations(cache);
    }

    private static Task<TradeRecommendation?> FindBySourceSignalAsync(
        AppDbContext db,
        long? sourceSignalId,
        CancellationToken ct) => db.TradeRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                recommendation => !recommendation.IsSuperseded
                    && recommendation.SourceSignalId == sourceSignalId,
                ct);

    private static void CopyPersistedIdentity(
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
}

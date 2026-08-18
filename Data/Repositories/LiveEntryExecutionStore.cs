using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class LiveEntryExecutionStore(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache)
    : ILiveEntryExecutionStore
{
    public async Task CommitAcceptedEntryAsync(
        TradeRecommendation recommendation,
        Position position,
        CancellationToken ct = default)
    {
        if (recommendation.Id <= 0)
            throw new InvalidOperationException(
                "A recommendation must be persisted before broker submission.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var stored = await db.TradeRecommendations.SingleOrDefaultAsync(
            item => item.Id == recommendation.Id,
            ct) ?? throw new InvalidOperationException(
                $"Recommendation {recommendation.Id} was not found.");
        if (stored.WasExecuted)
            throw new InvalidOperationException(
                $"Recommendation {recommendation.Id} was already committed.");

        stored.WasExecuted = true;
        db.Positions.Add(position);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        recommendation.WasExecuted = true;
        TradeReadCache.InvalidateAcceptedEntry(cache);
    }
}

using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Trading;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class TradeHistoryStore(IDbContextFactory<AppDbContext> dbFactory)
    : ITradeHistoryStore
{
    public async Task<List<TradeRecord>> GetTradesAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 1000,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = BuildQuery(db, patternType, from, to);
        if (skip > 0)
            query = query.Skip(skip);
        return await query.Take(take > 0 ? take : 1000).ToListAsync(ct);
    }

    public async Task<int> GetTradeCountAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Filter(db.TradeRecords.AsNoTracking(), patternType, from, to)
            .CountAsync(ct);
    }

    public async Task<List<TradeRecord>> GetRecentAsync(
        int limit = 5000,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeRecords
            .AsNoTracking()
            .OrderByDescending(trade => trade.EntryTime)
            .Take(limit)
            .ToListAsync(ct);
    }

    private static IQueryable<TradeRecord> BuildQuery(
        AppDbContext db,
        PatternType? patternType,
        DateTime? from,
        DateTime? to) => Filter(
            db.TradeRecords.AsNoTracking(), patternType, from, to)
        .OrderByDescending(trade => trade.EntryTime);

    private static IQueryable<TradeRecord> Filter(
        IQueryable<TradeRecord> query,
        PatternType? patternType,
        DateTime? from,
        DateTime? to)
    {
        if (patternType.HasValue)
            query = query.Where(trade => trade.PatternType == patternType.Value);
        if (from.HasValue)
            query = query.Where(trade => trade.EntryTime >= from.Value);
        if (to.HasValue)
            query = query.Where(trade => trade.ExitTime <= to.Value);
        return query;
    }
}

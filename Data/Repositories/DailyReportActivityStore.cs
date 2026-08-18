using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Reporting;

namespace StockTrader.Data.Repositories;

public sealed class DailyReportActivityStore(
    IDbContextFactory<AppDbContext> dbFactory) : IDailyReportActivityStore
{
    public async Task<DailyReportActivitySnapshot> ReadAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var trades = await db.TradeRecords
            .AsNoTracking()
            .Where(trade => trade.ExitTime >= fromUtc && trade.ExitTime < toUtc)
            .OrderBy(trade => trade.ExitTime)
            .ThenBy(trade => trade.Id)
            .Select(trade => new DailyReportTradeSnapshot(
                trade.Symbol,
                trade.EntryPrice,
                trade.Quantity,
                trade.PnL,
                trade.ExitTime))
            .ToArrayAsync(ct);
        var signals = await db.TradeRecommendations
            .AsNoTracking()
            .Where(signal => signal.GeneratedAt >= fromUtc && signal.GeneratedAt < toUtc)
            .OrderByDescending(signal => signal.GeneratedAt)
            .ThenByDescending(signal => signal.Id)
            .Select(signal => new DailyReportSignalSnapshot(
                signal.Symbol,
                signal.PatternType,
                signal.EntryPrice,
                signal.GeneratedAt))
            .ToArrayAsync(ct);

        return new DailyReportActivitySnapshot(trades, signals);
    }
}

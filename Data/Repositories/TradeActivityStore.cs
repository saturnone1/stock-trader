using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Trading;

namespace StockTrader.Data.Repositories;

public sealed class TradeActivityStore(IDbContextFactory<AppDbContext> dbFactory)
    : ITradeActivityStore
{
    public async Task<IReadOnlyList<TradeRecommendationActivity>> GetRecommendationsAsync(
        int count,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradeRecommendations
            .AsNoTracking()
            .OrderByDescending(row => row.GeneratedAt)
            .ThenByDescending(row => row.Id)
            .Take(count)
            .Select(row => new TradeRecommendationActivity(
                row.Id,
                row.SourceSignalId,
                row.Symbol,
                row.PatternType,
                row.CustomPatternName,
                row.EntryPrice,
                row.StopLossPrice,
                row.TargetPrice,
                row.PositionSize,
                row.ShareQuantity,
                row.Expectancy,
                row.WasExecuted,
                row.Mode,
                row.GeneratedAt,
                row.EntryRequestedAt,
                row.EntryAccountId,
                row.EntryOrderId != null && row.EntryOrderId != "",
                row.EntryExecutionNote))
            .ToArrayAsync(ct);
    }

    public async Task<TradeHistorySlice> GetHistoryAsync(
        PatternType? patternType,
        DateTime? from,
        DateTime? to,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var filtered = db.TradeRecords.AsNoTracking();
        if (patternType.HasValue)
            filtered = filtered.Where(row => row.PatternType == patternType.Value);
        if (from.HasValue)
            filtered = filtered.Where(row => row.EntryTime >= from.Value);
        if (to.HasValue)
            filtered = filtered.Where(row => row.ExitTime <= to.Value);

        var totalCount = await filtered.CountAsync(ct);
        var trades = await filtered
            .OrderByDescending(row => row.EntryTime)
            .ThenByDescending(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => new CompletedTradeActivity(
                row.Id,
                row.Symbol,
                row.PatternType,
                row.CustomPatternName,
                row.EntryPrice,
                row.ExitPrice,
                row.Quantity,
                row.PnL,
                row.PnLPercent,
                row.ExitReason,
                row.EntryTime,
                row.ExitTime))
            .ToArrayAsync(ct);
        return new(totalCount, trades);
    }
}

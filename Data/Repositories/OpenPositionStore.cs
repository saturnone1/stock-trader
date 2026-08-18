using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Trading;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class OpenPositionStore(IDbContextFactory<AppDbContext> dbFactory)
    : IOpenPositionStore
{
    public async Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Positions
            .AsNoTracking()
            .Include(position => position.ScalingExecutions)
            .Where(position => position.ClosedAt == null)
            .OrderByDescending(position => position.OpenedAt)
            .ToListAsync(ct);
    }

    public async Task SavePositionAsync(Position position, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (position.Id == 0)
            db.Positions.Add(position);
        else
            db.Positions.Update(position);
        await db.SaveChangesAsync(ct);
    }
}

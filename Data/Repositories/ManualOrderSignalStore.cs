using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class ManualOrderSignalStore(IDbContextFactory<AppDbContext> dbFactory)
    : IManualOrderSignalStore
{
    public async Task<PatternSignal?> LoadAsync(
        long signalId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PatternSignals
            .AsNoTracking()
            .SingleOrDefaultAsync(signal => signal.Id == signalId, ct);
    }
}

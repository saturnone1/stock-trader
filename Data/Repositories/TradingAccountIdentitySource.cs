using Microsoft.EntityFrameworkCore;
using StockTrader.Application.TradingCore;

namespace StockTrader.Data.Repositories;

internal sealed class TradingAccountIdentitySource(
    IDbContextFactory<AppDbContext> dbFactory) : ITradingAccountIdentitySource
{
    public async Task<string?> GetActiveAccountIdAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.TradingAccounts.AsNoTracking()
            .Where(account => account.IsEnabled && account.IsActive)
            .Select(account => (int?)account.Id)
            .SingleOrDefaultAsync(ct);
        return id?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

using Microsoft.EntityFrameworkCore;

namespace StockTrader.Data.Migrations;

public sealed class DatabaseMigrationStatusProvider
{
    private readonly AppDbContext _db;

    public DatabaseMigrationStatusProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DatabaseMigrationStatus> GetAsync(CancellationToken ct = default)
    {
        var known = _db.Database.GetMigrations().ToArray();
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var appliedSet = applied.ToHashSet(StringComparer.Ordinal);
        var pending = known.Where(value => !appliedSet.Contains(value)).ToArray();
        return new DatabaseMigrationStatus(
            Current: applied.LastOrDefault(),
            Latest: known.LastOrDefault(),
            PendingCount: pending.Length,
            IsSynchronized: known.Length > 0 && pending.Length == 0);
    }
}

public sealed record DatabaseMigrationStatus(
    string? Current,
    string? Latest,
    int PendingCount,
    bool IsSynchronized);

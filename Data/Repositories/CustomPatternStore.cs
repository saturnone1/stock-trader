using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class CustomPatternStore : ICustomPatternStore
{
    private readonly AppDbContext _db;

    public CustomPatternStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<StoredStrategy>> ListAsync(CancellationToken ct = default) =>
        (await _db.CustomPatterns.AsNoTracking()
            .OrderByDescending(pattern => pattern.UpdatedAt)
            .ToListAsync(ct)).Select(pattern => pattern.ToStoredStrategy()).ToArray();

    public async Task<StoredStrategy?> FindAsync(int id, CancellationToken ct = default) =>
        (await _db.CustomPatterns.AsNoTracking().FirstOrDefaultAsync(pattern => pattern.Id == id, ct))
            ?.ToStoredStrategy();

    public async Task<StoredStrategy?> FindByNameAsync(string normalizedName, CancellationToken ct = default) =>
        (await _db.CustomPatterns.AsNoTracking()
            .FirstOrDefaultAsync(pattern => pattern.NormalizedName == normalizedName, ct))
            ?.ToStoredStrategy();

    public Task<bool> NameExistsAsync(
        string normalizedName,
        int? excludingId = null,
        CancellationToken ct = default) =>
        _db.CustomPatterns.AnyAsync(pattern =>
            (!excludingId.HasValue || pattern.Id != excludingId.Value)
            && pattern.NormalizedName == normalizedName, ct);

    public async Task<CustomPatternStoreWriteOutcome> AddAsync(
        StoredStrategy strategy,
        CancellationToken ct = default)
    {
        var definition = strategy.ToEntity();
        definition.NormalizedName = StoredStrategyName.Normalize(definition.Name);
        _db.CustomPatterns.Add(definition);
        return await SaveAsync(definition, ct);
    }

    public async Task<CustomPatternStoreWriteOutcome> UpdateAsync(
        StoredStrategy strategy,
        CancellationToken ct = default)
    {
        var definition = strategy.ToEntity();
        definition.NormalizedName = StoredStrategyName.Normalize(definition.Name);
        _db.CustomPatterns.Update(definition);
        return await SaveAsync(definition, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        await _db.CustomPatterns.Where(pattern => pattern.Id == id).ExecuteDeleteAsync(ct) > 0;

    private async Task<CustomPatternStoreWriteOutcome> SaveAsync(
        CustomPatternDefinition definition,
        CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            var saved = definition.ToStoredStrategy();
            _db.Entry(definition).State = EntityState.Detached;
            return CustomPatternStoreWriteOutcome.Saved(saved);
        }
        catch (DbUpdateException exception) when (IsNormalizedNameConflict(exception))
        {
            _db.Entry(definition).State = EntityState.Detached;
            return CustomPatternStoreWriteOutcome.NameConflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(definition).State = EntityState.Detached;
            return CustomPatternStoreWriteOutcome.NotFound();
        }
    }

    private static bool IsNormalizedNameConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        } sqliteException
        && sqliteException.Message.Contains(
            "CustomPatterns.NormalizedName",
            StringComparison.Ordinal);
}

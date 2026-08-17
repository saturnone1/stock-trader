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

    public async Task<IReadOnlyList<CustomPatternDefinition>> ListAsync(CancellationToken ct = default) =>
        await _db.CustomPatterns.AsNoTracking()
            .OrderByDescending(pattern => pattern.UpdatedAt)
            .ToListAsync(ct);

    public Task<CustomPatternDefinition?> FindAsync(int id, CancellationToken ct = default) =>
        _db.CustomPatterns.AsNoTracking().FirstOrDefaultAsync(pattern => pattern.Id == id, ct);

    public Task<CustomPatternDefinition?> FindByNameAsync(string normalizedName, CancellationToken ct = default) =>
        _db.CustomPatterns.AsNoTracking()
            .FirstOrDefaultAsync(pattern => pattern.NormalizedName == normalizedName, ct);

    public Task<bool> NameExistsAsync(
        string normalizedName,
        int? excludingId = null,
        CancellationToken ct = default) =>
        _db.CustomPatterns.AnyAsync(pattern =>
            (!excludingId.HasValue || pattern.Id != excludingId.Value)
            && pattern.NormalizedName == normalizedName, ct);

    public async Task<CustomPatternWriteResult> AddAsync(
        CustomPatternDefinition definition,
        CancellationToken ct = default)
    {
        definition.NormalizedName = StoredStrategyName.Normalize(definition.Name);
        _db.CustomPatterns.Add(definition);
        return await SaveAsync(definition, ct);
    }

    public async Task<CustomPatternWriteResult> UpdateAsync(
        CustomPatternDefinition definition,
        CancellationToken ct = default)
    {
        definition.NormalizedName = StoredStrategyName.Normalize(definition.Name);
        _db.CustomPatterns.Update(definition);
        return await SaveAsync(definition, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        await _db.CustomPatterns.Where(pattern => pattern.Id == id).ExecuteDeleteAsync(ct) > 0;

    private async Task<CustomPatternWriteResult> SaveAsync(
        CustomPatternDefinition definition,
        CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return CustomPatternWriteResult.Saved;
        }
        catch (DbUpdateException exception) when (IsNormalizedNameConflict(exception))
        {
            _db.Entry(definition).State = EntityState.Detached;
            return CustomPatternWriteResult.NameConflict;
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

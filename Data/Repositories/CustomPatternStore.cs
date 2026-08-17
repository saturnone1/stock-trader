using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Strategies;
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

    public Task<CustomPatternDefinition?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var normalizedName = name.Trim().ToLowerInvariant();
        return _db.CustomPatterns.AsNoTracking()
            .FirstOrDefaultAsync(pattern => pattern.Name.ToLower() == normalizedName, ct);
    }

    public Task<bool> NameExistsAsync(
        string normalizedName,
        int? excludingId = null,
        CancellationToken ct = default) =>
        _db.CustomPatterns.AnyAsync(pattern =>
            (!excludingId.HasValue || pattern.Id != excludingId.Value)
            && pattern.Name.ToLower() == normalizedName, ct);

    public async Task AddAsync(CustomPatternDefinition definition, CancellationToken ct = default)
    {
        _db.CustomPatterns.Add(definition);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CustomPatternDefinition definition, CancellationToken ct = default)
    {
        _db.CustomPatterns.Update(definition);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default) =>
        await _db.CustomPatterns.Where(pattern => pattern.Id == id).ExecuteDeleteAsync(ct) > 0;
}

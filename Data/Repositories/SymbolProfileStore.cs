using Microsoft.EntityFrameworkCore;
using StockTrader.Application.SymbolProfiles;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>종목별 전략 배정 유스케이스의 EF Core 저장소 어댑터입니다.</summary>
public sealed class SymbolProfileStore(AppDbContext db) : ISymbolProfileStore
{
    public async Task<IReadOnlyList<ManagedSymbolProfile>> ListAsync(
        string? normalizedSymbol,
        CancellationToken ct = default)
    {
        var query = db.SymbolProfiles.AsNoTracking();
        if (normalizedSymbol is not null)
            query = query.Where(profile => profile.Symbol == normalizedSymbol);

        return (await query
                .OrderBy(profile => profile.Symbol)
                .ThenByDescending(profile => profile.IsActive)
                .ThenBy(profile => profile.Name)
                .ToListAsync(ct))
            .Select(ToSnapshot)
            .ToArray();
    }

    public async Task<ManagedSymbolProfile?> GetActiveAsync(
        string normalizedSymbol,
        CancellationToken ct = default)
    {
        var profile = await db.SymbolProfiles
            .AsNoTracking()
            .Where(item => item.Symbol == normalizedSymbol && item.IsActive)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefaultAsync(ct);
        return profile is null ? null : ToSnapshot(profile);
    }

    public async Task<ManagedSymbolProfile?> GetBySymbolAndNameAsync(
        string normalizedSymbol,
        string name,
        CancellationToken ct = default)
    {
        var profile = await db.SymbolProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Symbol == normalizedSymbol && item.Name == name,
                ct);
        return profile is null ? null : ToSnapshot(profile);
    }

    public async Task<ManagedSymbolProfile> SaveAsync(
        ManagedSymbolProfile profile,
        CancellationToken ct = default)
    {
        SymbolProfile entity;
        if (profile.Id == 0)
        {
            entity = new SymbolProfile();
            db.SymbolProfiles.Add(entity);
        }
        else
        {
            entity = await db.SymbolProfiles.SingleAsync(item => item.Id == profile.Id, ct);
        }

        Apply(profile, entity);
        await db.SaveChangesAsync(ct);
        return ToSnapshot(entity);
    }

    public async Task<ManagedSymbolProfile?> SetActiveAsync(
        long id,
        bool isActive,
        DateTime updatedAt,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var profile = await db.SymbolProfiles.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (profile is null)
            return null;

        if (isActive)
        {
            await db.SymbolProfiles
                .Where(item => item.Symbol == profile.Symbol && item.IsActive && item.Id != profile.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.IsActive, false)
                        .SetProperty(item => item.UpdatedAt, updatedAt),
                    ct);
        }

        profile.IsActive = isActive;
        profile.UpdatedAt = updatedAt;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToSnapshot(profile);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var profile = await db.SymbolProfiles.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (profile is null)
            return false;
        db.SymbolProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static ManagedSymbolProfile ToSnapshot(SymbolProfile value) => new()
    {
        Id = value.Id,
        Symbol = value.Symbol,
        Name = value.Name,
        IsActive = value.IsActive,
        EnabledPatterns = value.EnabledPatterns.ToArray(),
        ParameterOverridesJson = value.ParameterOverridesJson,
        WeightStrategyJson = value.WeightStrategyJson,
        RiskPerTradePercent = value.RiskPerTradePercent,
        MaxTotalPositions = value.MaxTotalPositions,
        BacktestReturnPct = value.BacktestReturnPct,
        BacktestWinRate = value.BacktestWinRate,
        BacktestMaxDrawdown = value.BacktestMaxDrawdown,
        BacktestSharpe = value.BacktestSharpe,
        BacktestTrades = value.BacktestTrades,
        BacktestFrom = value.BacktestFrom,
        BacktestTo = value.BacktestTo,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    private static void Apply(ManagedSymbolProfile source, SymbolProfile target)
    {
        target.Symbol = source.Symbol;
        target.Name = source.Name;
        target.IsActive = source.IsActive;
        target.EnabledPatterns = source.EnabledPatterns.ToList();
        target.ParameterOverridesJson = source.ParameterOverridesJson;
        target.WeightStrategyJson = source.WeightStrategyJson;
        target.RiskPerTradePercent = source.RiskPerTradePercent;
        target.MaxTotalPositions = source.MaxTotalPositions;
        target.BacktestReturnPct = source.BacktestReturnPct;
        target.BacktestWinRate = source.BacktestWinRate;
        target.BacktestMaxDrawdown = source.BacktestMaxDrawdown;
        target.BacktestSharpe = source.BacktestSharpe;
        target.BacktestTrades = source.BacktestTrades;
        target.BacktestFrom = source.BacktestFrom;
        target.BacktestTo = source.BacktestTo;
        target.CreatedAt = source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt;
    }
}

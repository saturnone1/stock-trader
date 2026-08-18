using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Accounts;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>거래 계좌 엔티티와 단일 활성 계좌 트랜잭션을 소유하는 EF Core 어댑터입니다.</summary>
public sealed class TradingAccountStore(IDbContextFactory<AppDbContext> dbFactory)
    : ITradingAccountStore
{
    public async Task<IReadOnlyList<ManagedTradingAccount>> LoadAllAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts
            .AsNoTracking()
            .OrderBy(account => account.CreatedAt)
            .ThenBy(account => account.Id)
            .Select(ToManagedExpression())
            .ToListAsync(ct);
    }

    public async Task<ManagedTradingAccount?> LoadActiveAsync(
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts
            .AsNoTracking()
            .Where(account => account.IsActive && account.IsEnabled)
            .OrderBy(account => account.CreatedAt)
            .ThenBy(account => account.Id)
            .Select(ToManagedExpression())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ManagedTradingAccount?> LoadByIdAsync(
        int accountId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TradingAccounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Select(ToManagedExpression())
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ManagedTradingAccount> AddAsync(
        ManagedTradingAccount account,
        DateTime modifiedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var isFirst = !await db.TradingAccounts.AnyAsync(ct);
        var shouldActivate = account.IsEnabled && (isFirst || account.IsActive);
        if (shouldActivate)
        {
            await db.TradingAccounts
                .Where(item => item.IsActive)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(item => item.IsActive, false),
                    ct);
        }

        var entity = ToEntity(account with
        {
            Id = 0,
            IsActive = shouldActivate,
            CreatedAt = modifiedAt,
            UpdatedAt = modifiedAt,
            LastConnectedAt = null
        });
        db.TradingAccounts.Add(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToManaged(entity);
    }

    public async Task<ManagedTradingAccount?> UpdateAsync(
        ManagedTradingAccount account,
        DateTime modifiedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.TradingAccounts.SingleOrDefaultAsync(
            item => item.Id == account.Id,
            ct);
        if (entity is null)
            return null;

        var shouldActivate = account.IsActive && account.IsEnabled;
        if (shouldActivate)
        {
            await db.TradingAccounts
                .Where(item => item.Id != account.Id && item.IsActive)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(item => item.IsActive, false),
                    ct);
        }

        entity.AccountName = account.AccountName;
        entity.BrokerType = account.BrokerType;
        entity.ApiKey = account.ApiKey ?? string.Empty;
        entity.ApiSecret = account.ApiSecret ?? string.Empty;
        entity.Environment = account.Environment;
        entity.IsActive = shouldActivate;
        entity.IsEnabled = account.IsEnabled;
        entity.Notes = account.Notes ?? string.Empty;
        entity.UpdatedAt = modifiedAt;
        entity.LastConnectedAt = account.LastConnectedAt;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToManaged(entity);
    }

    public async Task<TradingAccountDeletion> DeleteAsync(
        int accountId,
        DateTime modifiedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.TradingAccounts.SingleOrDefaultAsync(
            account => account.Id == accountId,
            ct);
        if (entity is null)
            return new TradingAccountDeletion(false, false, null);

        var wasActive = entity.IsActive;
        db.TradingAccounts.Remove(entity);
        await db.SaveChangesAsync(ct);
        int? activatedId = null;
        if (wasActive)
        {
            await db.TradingAccounts
                .Where(account => account.IsActive)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(account => account.IsActive, false),
                    ct);
            var next = await db.TradingAccounts
                .Where(account => account.IsEnabled)
                .OrderBy(account => account.CreatedAt)
                .ThenBy(account => account.Id)
                .FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsActive = true;
                next.UpdatedAt = modifiedAt;
                activatedId = next.Id;
                await db.SaveChangesAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
        return new TradingAccountDeletion(true, wasActive, activatedId);
    }

    public async Task<bool> SetActiveAsync(
        int accountId,
        DateTime modifiedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var target = await db.TradingAccounts.SingleOrDefaultAsync(
            account => account.Id == accountId && account.IsEnabled,
            ct);
        if (target is null)
            return false;

        await db.TradingAccounts
            .Where(account => account.Id != accountId && account.IsActive)
            .ExecuteUpdateAsync(
                update => update.SetProperty(account => account.IsActive, false),
                ct);
        target.IsActive = true;
        target.UpdatedAt = modifiedAt;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task TouchLastConnectedAsync(
        int accountId,
        DateTime connectedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TradingAccounts
            .Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    account => account.LastConnectedAt,
                    connectedAt),
                ct);
    }

    private static Expression<Func<TradingAccount, ManagedTradingAccount>>
        ToManagedExpression() => account => new ManagedTradingAccount
        {
            Id = account.Id,
            AccountName = account.AccountName,
            BrokerType = account.BrokerType,
            ApiKey = account.ApiKey,
            ApiSecret = account.ApiSecret,
            Environment = account.Environment,
            IsActive = account.IsActive,
            IsEnabled = account.IsEnabled,
            Notes = account.Notes,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            LastConnectedAt = account.LastConnectedAt
        };

    private static ManagedTradingAccount ToManaged(TradingAccount account) => new()
    {
        Id = account.Id,
        AccountName = account.AccountName,
        BrokerType = account.BrokerType,
        ApiKey = account.ApiKey ?? string.Empty,
        ApiSecret = account.ApiSecret ?? string.Empty,
        Environment = account.Environment,
        IsActive = account.IsActive,
        IsEnabled = account.IsEnabled,
        Notes = account.Notes ?? string.Empty,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt,
        LastConnectedAt = account.LastConnectedAt
    };

    private static TradingAccount ToEntity(ManagedTradingAccount account) => new()
    {
        Id = account.Id,
        AccountName = account.AccountName,
        BrokerType = account.BrokerType,
        ApiKey = account.ApiKey ?? string.Empty,
        ApiSecret = account.ApiSecret ?? string.Empty,
        Environment = account.Environment ?? string.Empty,
        IsActive = account.IsActive,
        IsEnabled = account.IsEnabled,
        Notes = account.Notes ?? string.Empty,
        CreatedAt = account.CreatedAt,
        UpdatedAt = account.UpdatedAt,
        LastConnectedAt = account.LastConnectedAt
    };
}

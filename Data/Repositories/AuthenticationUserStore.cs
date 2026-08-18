using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Authentication;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class AuthenticationUserStore(IDbContextFactory<AppDbContext> dbFactory)
    : IAuthenticationUserStore
{
    public async Task<bool> HasAnyAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AppUsers.AnyAsync(ct);
    }

    public async Task<AuthenticationUser?> FindByUsernameAsync(
        string username,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = username.ToUpperInvariant();
        return await db.AppUsers
            .AsNoTracking()
            .Where(user => user.Username.ToUpper() == normalized)
            .Select(ToAuthenticationUser())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuthenticationUser?> FindByIdAsync(
        int userId,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AppUsers
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(ToAuthenticationUser())
            .SingleOrDefaultAsync(ct);
    }

    public async Task<AuthenticationUserCreation> TryCreateAsync(
        NewAuthenticationUser user,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.AppUsers.AnyAsync(
                existing => existing.Username.ToUpper() == user.Username.ToUpper(),
                ct))
        {
            return new(AuthenticationUserCreationStatus.UsernameConflict);
        }

        var entity = new AppUser
        {
            Username = user.Username,
            PasswordHash = user.PasswordHash,
            Salt = user.Salt,
            CreatedAt = user.CreatedAt,
            IsActive = true
        };
        db.AppUsers.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(AuthenticationUserCreationStatus.Created, entity.Id);
        }
        catch (DbUpdateException)
        {
            if (await UsernameExistsAsync(user.Username, ct))
                return new(AuthenticationUserCreationStatus.UsernameConflict);
            throw;
        }
    }

    public async Task<AuthenticationLoginFailure> RecordFailedLoginAsync(
        int userId,
        DateTime observedAt,
        int maximumFailedLoginAttempts,
        DateTime lockoutUntil,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.AppUsers
            .Where(entity => entity.Id == userId
                && (!entity.LockedUntil.HasValue || entity.LockedUntil <= observedAt))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        entity => entity.LockedUntil,
                        entity => entity.FailedLoginAttempts + 1 >= maximumFailedLoginAttempts
                            ? lockoutUntil
                            : null)
                    .SetProperty(
                        entity => entity.FailedLoginAttempts,
                        entity => entity.FailedLoginAttempts + 1 >= maximumFailedLoginAttempts
                            ? 0
                            : entity.FailedLoginAttempts + 1),
                ct);
        var current = await db.AppUsers
            .AsNoTracking()
            .Where(entity => entity.Id == userId)
            .Select(entity => new
            {
                entity.FailedLoginAttempts,
                entity.LockedUntil
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Authentication user {userId} no longer exists.");
        return new(
            current.FailedLoginAttempts,
            current.LockedUntil,
            updated == 1 && current.LockedUntil == lockoutUntil);
    }

    public async Task<AuthenticationLoginSuccess> RecordSuccessfulLoginAsync(
        int userId,
        DateTime observedAt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.AppUsers
            .Where(entity => entity.Id == userId
                && (!entity.LockedUntil.HasValue || entity.LockedUntil <= observedAt))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.FailedLoginAttempts, 0)
                    .SetProperty(entity => entity.LockedUntil, (DateTime?)null)
                    .SetProperty(entity => entity.LastLoginAt, observedAt),
                ct);
        if (updated == 1)
            return new(true, null);

        var lockedUntil = await db.AppUsers
            .AsNoTracking()
            .Where(entity => entity.Id == userId)
            .Select(entity => entity.LockedUntil)
            .SingleOrDefaultAsync(ct);
        if (lockedUntil is null)
            throw new InvalidOperationException($"Authentication user {userId} no longer exists.");
        return new(false, lockedUntil);
    }

    public async Task SavePasswordAsync(
        int userId,
        string passwordHash,
        string salt,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.AppUsers
            .Where(entity => entity.Id == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.PasswordHash, passwordHash)
                    .SetProperty(entity => entity.Salt, salt),
                ct);
        if (updated != 1)
            throw new InvalidOperationException($"Authentication user {userId} no longer exists.");
    }

    private async Task<bool> UsernameExistsAsync(
        string username,
        CancellationToken ct)
    {
        await using var verification = await dbFactory.CreateDbContextAsync(ct);
        return await verification.AppUsers.AnyAsync(
            existing => existing.Username.ToUpper() == username.ToUpper(),
            ct);
    }

    private static System.Linq.Expressions.Expression<Func<AppUser, AuthenticationUser>>
        ToAuthenticationUser() => user => new(
            user.Id,
            user.Username,
            user.PasswordHash,
            user.Salt,
            user.CreatedAt,
            user.LastLoginAt,
            user.IsActive,
            user.FailedLoginAttempts,
            user.LockedUntil);
}

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Authentication;
using StockTrader.Data;
using StockTrader.Data.Repositories;

namespace StockTrader.Tests;

public sealed class AuthenticationUserStoreTests
{
    [Fact]
    public async Task StoreMapsCredentialAndLoginStateWithoutExposingEfEntities()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AuthenticationUserStore(database.Factory);
        var createdAt = new DateTime(2026, 8, 19, 2, 0, 0, DateTimeKind.Utc);
        var creation = await store.TryCreateAsync(
            new("Trader", "hash", "salt", createdAt));

        creation.Status.Should().Be(AuthenticationUserCreationStatus.Created);
        var user = await store.FindByUsernameAsync("tRaDeR");
        user.Should().NotBeNull();
        user!.CreatedAt.Should().Be(createdAt);

        var lockedUntil = createdAt.AddMinutes(15);
        await store.RecordFailedLoginAsync(
            user.Id,
            createdAt,
            1,
            lockedUntil);
        (await store.RecordSuccessfulLoginAsync(user.Id, createdAt.AddMinutes(1)))
            .Accepted.Should().BeFalse();
        await store.SavePasswordAsync(user.Id, "new-hash", "new-salt");

        var persisted = await store.FindByIdAsync(user.Id);
        persisted!.FailedLoginAttempts.Should().Be(0);
        persisted.LockedUntil.Should().Be(lockedUntil);
        persisted.LastLoginAt.Should().BeNull();
        persisted.PasswordHash.Should().Be("new-hash");
        persisted.Salt.Should().Be("new-salt");
    }

    [Fact]
    public async Task LateFailureCannotClearAnExistingLock()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AuthenticationUserStore(database.Factory);
        var now = new DateTime(2026, 8, 19, 3, 30, 0, DateTimeKind.Utc);
        var creation = await store.TryCreateAsync(new("Trader", "hash", "salt", now));
        var lockedUntil = now.AddMinutes(15);

        var locked = await store.RecordFailedLoginAsync(
            creation.UserId,
            now,
            1,
            lockedUntil);
        var late = await store.RecordFailedLoginAsync(
            creation.UserId,
            now.AddSeconds(1),
            1,
            now.AddMinutes(16));

        locked.NewlyLocked.Should().BeTrue();
        late.NewlyLocked.Should().BeFalse();
        late.LockedUntil.Should().Be(lockedUntil);
        (await store.FindByIdAsync(creation.UserId))!.LockedUntil.Should().Be(lockedUntil);
    }

    [Fact]
    public async Task StoreRejectsCaseInsensitiveUsernameConflict()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AuthenticationUserStore(database.Factory);
        var now = new DateTime(2026, 8, 19, 3, 0, 0, DateTimeKind.Utc);

        (await store.TryCreateAsync(new("Trader", "one", "salt", now)))
            .Status.Should().Be(AuthenticationUserCreationStatus.Created);
        (await store.TryCreateAsync(new("TRADER", "two", "salt", now)))
            .Status.Should().Be(AuthenticationUserCreationStatus.UsernameConflict);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public TestFactory Factory { get; }

        private TestDatabase(SqliteConnection connection, TestFactory factory)
        {
            this.connection = connection;
            Factory = factory;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new TestFactory(options);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return new(connection, factory);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class TestFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

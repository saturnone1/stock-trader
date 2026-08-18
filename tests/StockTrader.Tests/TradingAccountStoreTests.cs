using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Accounts;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Trading;

namespace StockTrader.Tests;

public sealed class TradingAccountStoreTests
{
    [Fact]
    public async Task FirstEnabledAccountBecomesActiveAndSecondActivationIsAtomic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new TradingAccountStore(database.Factory);
        var firstAt = new DateTime(2026, 8, 18, 1, 0, 0, DateTimeKind.Utc);
        var secondAt = firstAt.AddMinutes(1);

        var first = await store.AddAsync(Account("First"), firstAt);
        var second = await store.AddAsync(Account("Second") with { IsActive = true }, secondAt);

        first.IsActive.Should().BeTrue();
        second.IsActive.Should().BeTrue();
        second.CreatedAt.Should().Be(secondAt);
        var rows = await store.LoadAllAsync();
        rows.Should().ContainSingle(item => item.IsActive)
            .Which.Id.Should().Be(second.Id);

        (await store.SetActiveAsync(first.Id, secondAt.AddMinutes(1))).Should().BeTrue();
        rows = await store.LoadAllAsync();
        rows.Should().ContainSingle(item => item.IsActive)
            .Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task DisabledAccountCannotBecomeActive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new TradingAccountStore(database.Factory);
        var now = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);
        var disabled = await store.AddAsync(
            Account("Disabled") with { IsEnabled = false, IsActive = true }, now);

        disabled.IsActive.Should().BeFalse();
        (await store.SetActiveAsync(disabled.Id, now.AddMinutes(1))).Should().BeFalse();
        var updated = await store.UpdateAsync(
            disabled with { IsActive = true }, now.AddMinutes(2));
        updated!.IsActive.Should().BeFalse();
        (await store.LoadActiveAsync()).Should().BeNull();
    }

    [Fact]
    public async Task DeletingActiveAccountPromotesEarliestEnabledAccount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new TradingAccountStore(database.Factory);
        var now = new DateTime(2026, 8, 18, 3, 0, 0, DateTimeKind.Utc);
        var active = await store.AddAsync(Account("Active"), now);
        var next = await store.AddAsync(Account("Next"), now.AddMinutes(1));
        await store.AddAsync(
            Account("Disabled") with { IsEnabled = false }, now.AddMinutes(2));

        var deletion = await store.DeleteAsync(active.Id, now.AddMinutes(3));

        deletion.Should().Be(new TradingAccountDeletion(true, true, next.Id));
        var promoted = await store.LoadActiveAsync();
        promoted.Should().NotBeNull();
        promoted!.Id.Should().Be(next.Id);
        promoted.UpdatedAt.Should().Be(now.AddMinutes(3));
    }

    [Fact]
    public async Task ConnectionTimestampIsPersisted()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new TradingAccountStore(database.Factory);
        var now = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);
        var account = await store.AddAsync(Account("Connected"), now);

        await store.TouchLastConnectedAsync(account.Id, now.AddSeconds(30));

        (await store.LoadByIdAsync(account.Id))!.LastConnectedAt
            .Should().Be(now.AddSeconds(30));
    }

    private static ManagedTradingAccount Account(string name) => new()
    {
        AccountName = name,
        BrokerType = BrokerType.Alpaca,
        ApiKey = "key",
        ApiSecret = "secret",
        Environment = "Paper",
        IsEnabled = true
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public TestFactory Factory { get; }

        private TestDatabase(SqliteConnection connection, TestFactory factory)
        {
            _connection = connection;
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
            return new TestDatabase(connection, factory);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
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

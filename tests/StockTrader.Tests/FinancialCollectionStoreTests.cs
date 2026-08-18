using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class FinancialCollectionStoreTests
{
    [Fact]
    public async Task RunLifecyclePreservesIdempotencyRestartAndSuccessMeanings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        await using (var seed = factory.CreateDbContext())
            await seed.Database.EnsureCreatedAsync();
        var store = new FinancialCollectionStore(factory);
        var firstStart = new DateTime(2026, 8, 18, 1, 0, 0);

        var firstId = await store.StartOrRestartRunAsync(
            "CSV", "input.csv", "fingerprint", firstStart);
        await store.FailRunAsync(firstId, "broken", firstStart.AddMinutes(1));
        var restartedId = await store.StartOrRestartRunAsync(
            "CSV", "input.csv", "fingerprint", firstStart.AddMinutes(2));

        restartedId.Should().Be(firstId);
        await using (var verifyRestart = factory.CreateDbContext())
        {
            var restarted = await verifyRestart.FinancialImportRuns.SingleAsync();
            restarted.Status.Should().Be("Running");
            restarted.ErrorMessage.Should().BeNull();
            restarted.CompletedAt.Should().BeNull();
            restarted.ImportedCount.Should().Be(0);
        }

        await store.CompleteRunAsync(
            restartedId, importedCount: 0, skippedCount: 2, warning: null,
            firstStart.AddMinutes(3));
        (await store.HasCompletedRunAsync("input.csv", "fingerprint")).Should().BeTrue();
        (await store.GetLatestCompletedAtAsync("CSV", requireImportedItems: false))
            .Should().Be(firstStart.AddMinutes(3));
        (await store.GetLatestCompletedAtAsync("CSV", requireImportedItems: true))
            .Should().BeNull();

        var successfulId = await store.StartOrRestartRunAsync(
            "CSV", "other.csv", "other", firstStart.AddMinutes(4));
        await store.CompleteRunAsync(
            successfulId, importedCount: 1, skippedCount: 0, warning: "note",
            firstStart.AddMinutes(5));
        (await store.GetLatestCompletedAtAsync("CSV", requireImportedItems: true))
            .Should().Be(firstStart.AddMinutes(5));
    }

    [Fact]
    public async Task TickerQueriesApplyActiveRankingLimitAndCaseInsensitiveLookup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        await using (var seed = factory.CreateDbContext())
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Tickers.AddRange(
                new Ticker { Symbol = "QQQ", Name = "Nasdaq", MarketCap = 20m, IsActive = true },
                new Ticker { Symbol = "AAPL", Name = "Apple", MarketCap = 20m, IsActive = true },
                new Ticker { Symbol = "OLD", Name = "Inactive", MarketCap = 100m, IsActive = false });
            await seed.SaveChangesAsync();
        }
        var store = new FinancialCollectionStore(factory);

        var top = await store.LoadTopActiveTickersAsync(1);
        var selected = await store.LoadTickersAsync(["aapl", "qqq"]);

        top.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        selected.Keys.Should().BeEquivalentTo("AAPL", "QQQ");
        selected["aapl"].Name.Should().Be("Apple");
    }

    private static TestFactory Factory(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private sealed class TestFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

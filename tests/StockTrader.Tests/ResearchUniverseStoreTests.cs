using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Research;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class ResearchUniverseStoreTests
{
    [Fact]
    public async Task ReadModelsAreDetachedAndApplyActiveAndSuccessSemantics()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        await using (var seed = factory.CreateDbContext())
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Tickers.AddRange(
                new Ticker { Symbol = "AAPL", Name = "Apple", IsActive = true, MarketCap = 10m },
                new Ticker { Symbol = "OLD", Name = "Old", IsActive = false, MarketCap = 20m });
            seed.FinancialSnapshots.Add(new FinancialSnapshot
            {
                Symbol = "AAPL",
                AsOfDate = new DateTime(2025, 1, 1),
                PeRatio = 12m,
                UpdatedAt = new DateTime(2025, 1, 2)
            });
            seed.FinancialImportRuns.Add(new FinancialImportRun
            {
                SourceType = "CSV",
                FilePath = "success.csv",
                Fingerprint = "success-1",
                Status = "Completed",
                ImportedCount = 1,
                StartedAt = new DateTime(2025, 1, 1),
                CompletedAt = new DateTime(2025, 1, 1, 1, 0, 0)
            });
            for (var index = 0; index < 11; index++)
            {
                seed.FinancialImportRuns.Add(new FinancialImportRun
                {
                    SourceType = "SEC",
                    FilePath = $"sec-{index}",
                    Fingerprint = $"failed-{index}",
                    Status = "Failed",
                    StartedAt = new DateTime(2025, 1, 2).AddMinutes(index)
                });
            }
            await seed.SaveChangesAsync();
        }
        var store = new ResearchUniverseStore(factory);

        var tickers = await store.LoadActiveTickersAsync();
        var data = await store.LoadFinancialResearchDataAsync();
        var history = await store.LoadImportRunHistoryAsync(10);

        tickers.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        data.ActiveTickers.Should().ContainSingle();
        data.FinancialSnapshots.Should().ContainSingle().Which.PeRatio.Should().Be(12m);
        history.RecentRuns.Should().HaveCount(10)
            .And.OnlyContain(run => run.Status == "Failed");
        history.LatestSuccessfulRun.Should().NotBeNull();
        history.LatestSuccessfulRun!.ImportedCount.Should().Be(1);
    }

    [Fact]
    public async Task UpsertCreatesThenUpdatesBySymbolAndAsOfDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        await using (var seed = factory.CreateDbContext())
            await seed.Database.EnsureCreatedAsync();
        var store = new ResearchUniverseStore(factory);
        var asOf = new DateTime(2025, 1, 1);

        await store.UpsertFinancialSnapshotsAsync(
        [
            Snapshot(asOf, 5m, new DateTime(2025, 1, 31)),
            Snapshot(asOf, 10m, new DateTime(2025, 2, 1))
        ]);
        await store.UpsertFinancialSnapshotsAsync(
            [Snapshot(asOf, 15m, new DateTime(2025, 2, 2))]);

        await using var verify = factory.CreateDbContext();
        var entity = await verify.FinancialSnapshots.AsNoTracking().SingleAsync();
        entity.PeRatio.Should().Be(15m);
        entity.CreatedAt.Should().Be(new DateTime(2025, 2, 1));
        entity.UpdatedAt.Should().Be(new DateTime(2025, 2, 2));
    }

    private static ManagedFinancialSnapshot Snapshot(
        DateTime asOf,
        decimal peRatio,
        DateTime modifiedAt) => new(
        "AAPL",
        asOf,
        "Test",
        peRatio,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        modifiedAt);

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

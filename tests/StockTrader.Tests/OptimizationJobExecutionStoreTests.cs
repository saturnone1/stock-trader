using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobExecutionStoreTests
{
    [Fact]
    public async Task SaveChunkAsync_RetainsOnlyTheHighestRankedResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        var jobId = await SeedJobAsync(factory);
        var store = new OptimizationJobExecutionStore(factory);

        await store.SaveChunkAsync(
            jobId,
            [
                new OptimizeResultItem { SortinoRatio = 1.2m },
                new OptimizeResultItem { SortinoRatio = 2.4m }
            ],
            testedAtStart: 10,
            testedCombinations: 12,
            currentChunkIndex: 2,
            new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc),
            topResultsToKeep: 1,
            rankBy: "sortinoRatio");

        await using var verify = factory.CreateDbContext();
        var result = await verify.OptimizationResults.AsNoTracking().SingleAsync();
        result.SortinoRatio.Should().Be(2.4m);
        result.Rank.Should().Be(1);
    }

    [Fact]
    public async Task SaveChunkAsync_MapsResultsAndPersistsTheFollowingCheckpoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        var jobId = await SeedJobAsync(factory);
        var store = new OptimizationJobExecutionStore(factory);
        var observedAt = new DateTime(2026, 8, 18, 1, 2, 3, DateTimeKind.Utc);
        var result = new OptimizeResultItem
        {
            Params = new OptimizeParamSnapshot { AtrStopMultiplier = 2.5m },
            TotalReturn = 12m,
            SortinoRatio = 1.7m,
            SharpeRatio = 1.2m,
            MaxDrawdown = 8m,
            WinRate = 60m,
            TotalTrades = 17,
            ProfitFactor = 1.8m,
            CalmarRatio = 1.5m,
            AnnualizedReturn = 0.2m
        };

        await store.SaveChunkAsync(
            jobId, [result], 120, 121, 4, observedAt, 25, "sortinoRatio");

        await using var verify = factory.CreateDbContext();
        var entity = await verify.OptimizationResults.AsNoTracking().SingleAsync();
        entity.JobId.Should().Be(jobId);
        entity.TestedAtCombination.Should().Be(120);
        entity.DiscoveredAt.Should().Be(observedAt);
        entity.TotalReturn.Should().Be(result.TotalReturn);
        entity.TotalTrades.Should().Be(result.TotalTrades);
        entity.ParamsJson.Should().Contain("2.5");
        var job = await verify.OptimizationJobs.AsNoTracking().SingleAsync();
        job.TestedCombinations.Should().Be(121);
        job.CurrentChunkIndex.Should().Be(4);
    }

    [Fact]
    public async Task LoadTopCandidatesAsync_SkipsMalformedLegacyParameterJson()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = Factory(connection);
        var jobId = await SeedJobAsync(factory);
        await using (var seed = factory.CreateDbContext())
        {
            seed.OptimizationResults.AddRange(
                new OptimizationResult
                {
                    JobId = jobId, Rank = 1, ParamsJson = "{\"atrStopMultiplier\":2.5}"
                },
                new OptimizationResult { JobId = jobId, Rank = 2, ParamsJson = "not-json" });
            await seed.SaveChangesAsync();
        }
        var store = new OptimizationJobExecutionStore(factory);

        var candidates = await store.LoadTopCandidatesAsync(jobId, 5);

        candidates.Should().ContainSingle();
        candidates[0].Parameters.AtrStopMultiplier.Should().Be(2.5m);
    }

    private static TestFactory Factory(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static async Task<int> SeedJobAsync(TestFactory factory)
    {
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var job = new OptimizationJob { Name = "execution-store" };
        db.OptimizationJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private sealed class TestFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

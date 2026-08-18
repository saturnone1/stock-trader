using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class OptimizationAutoTuneStoreTests
{
    [Fact]
    public async Task RecordApplyOutcomeAsync_IncrementsWithoutLostUpdate()
    {
        await using var database = await SharedDatabase.OpenAsync();
        var jobId = await database.AddJobAsync();
        var first = new OptimizationAutoTuneStore(database.Factory);
        var second = new OptimizationAutoTuneStore(database.Factory);
        var observedAt = new DateTime(2026, 8, 18, 1, 0, 0, DateTimeKind.Utc);

        await Task.WhenAll(
            first.RecordApplyOutcomeAsync(jobId, 1, "first", observedAt, true),
            second.RecordApplyOutcomeAsync(jobId, 2, "second", observedAt, true));

        await using var verify = database.Factory.CreateDbContext();
        (await verify.OptimizationJobs.AsNoTracking().SingleAsync())
            .AppliedResultCount.Should().Be(2);
    }

    [Fact]
    public async Task FindCandidateAsync_PreservesIdentityAndRejectsMalformedParameters()
    {
        await using var database = await SharedDatabase.OpenAsync();
        var jobId = await database.AddJobAsync();
        await using (var seed = database.Factory.CreateDbContext())
        {
            seed.OptimizationResults.Add(new OptimizationResult
            {
                JobId = jobId,
                Rank = 1,
                ParamsJson = "not-json",
                TotalReturn = 12m,
                TotalTrades = 20
            });
            await seed.SaveChangesAsync();
        }

        var stored = await new OptimizationAutoTuneStore(database.Factory)
            .ListCandidatesAsync(jobId, 10);

        stored.Should().ContainSingle();
        stored[0].Id.Should().BeGreaterThan(0);
        stored[0].Parameters.Should().BeNull();
    }

    private sealed class SharedDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;

        private SharedDatabase(SqliteConnection keeper, TestDbContextFactory factory)
        {
            _keeper = keeper;
            Factory = factory;
        }

        public TestDbContextFactory Factory { get; }

        public static async Task<SharedDatabase> OpenAsync()
        {
            var connectionString = $"Data Source=autotune-store-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connectionString)
                .Options;
            var factory = new TestDbContextFactory(options);
            await using var setup = factory.CreateDbContext();
            await setup.Database.EnsureCreatedAsync();
            return new SharedDatabase(keeper, factory);
        }

        public async Task<int> AddJobAsync()
        {
            await using var db = Factory.CreateDbContext();
            var job = new OptimizationJob
            {
                Name = "auto-tune store",
                RequestJson = OptimizeRequestJsonCodec.Serialize(new OptimizeRequest())
            };
            db.OptimizationJobs.Add(job);
            await db.SaveChangesAsync();
            return job.Id;
        }

        public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

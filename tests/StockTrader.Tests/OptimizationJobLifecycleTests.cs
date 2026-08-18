using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobLifecycleTests
{
    [Fact]
    public async Task TryStartNextAsync_AllowsOnlyOneConcurrentClaim()
    {
        var connectionString = $"Data Source=lifecycle-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var factory = new Factory(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString).Options);
        await using (var seed = factory.CreateDbContext())
        {
            await seed.Database.EnsureCreatedAsync();
            seed.OptimizationJobs.Add(new OptimizationJob
            {
                Name = "single-claim",
                Status = OptimizationJobStatus.Pending,
                Priority = 10
            });
            await seed.SaveChangesAsync();
        }
        var first = new OptimizationJobLifecycle(factory);
        var second = new OptimizationJobLifecycle(factory);
        var observedAt = new DateTime(2026, 8, 18, 2, 30, 0, DateTimeKind.Utc);

        var claims = await Task.WhenAll(
            first.TryStartNextAsync(observedAt),
            second.TryStartNextAsync(observedAt));

        claims.Count(ticket => ticket != null).Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var saved = await verify.OptimizationJobs.AsNoTracking().SingleAsync();
        saved.Status.Should().Be(OptimizationJobStatus.Running);
        saved.StartedAt.Should().Be(observedAt);
    }

    [Fact]
    public async Task TryStartNextAsync_MarksRunningAndReturnsStorageIndependentTicket()
    {
        await using var database = await TestDatabase.OpenAsync();
        var stored = await database.AddAsync(new OptimizationJob
        {
            Name = "lifecycle",
            Priority = 4,
            RequestJson = "{\"symbols\":[\"TQQQ\"]}",
            TotalCombinations = 900,
            TestedCombinations = 200,
            CurrentChunkIndex = 2,
            ChunkSize = 100,
            MaxDurationHours = 3m,
            MaxTestedCombinations = 700,
            RankBy = "calmarRatio",
            TopResultsToKeep = 30
        });
        var lifecycle = new OptimizationJobLifecycle(database.Factory);
        var observedAt = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);

        var ticket = await lifecycle.TryStartNextAsync(observedAt);

        var saved = await database.FindAsync(stored.Id);
        saved!.Status.Should().Be(OptimizationJobStatus.Running);
        saved.StartedAt.Should().Be(observedAt);
        ticket.Should().NotBeNull();
        ticket!.Id.Should().Be(stored.Id);
        ticket.RequestJson.Should().Be(stored.RequestJson);
        ticket.TestedCombinations.Should().Be(200);
        ticket.CurrentChunkIndex.Should().Be(2);
        ticket.RankBy.Should().Be("calmarRatio");
        ticket.TopResultsToKeep.Should().Be(30);
    }

    [Theory]
    [InlineData(OptimizationJobExecutionDisposition.Completed, OptimizationJobStatus.Completed)]
    [InlineData(OptimizationJobExecutionDisposition.Cancelled, OptimizationJobStatus.Cancelled)]
    public async Task ApplyDispositionAsync_SetsTerminalStateAndClearsPreviousError(
        OptimizationJobExecutionDisposition disposition,
        OptimizationJobStatus expectedStatus)
    {
        await using var database = await TestDatabase.OpenAsync();
        var stored = await database.AddAsync(new OptimizationJob
        {
            Name = "terminal",
            Status = OptimizationJobStatus.Running,
            ErrorMessage = "previous"
        });
        var lifecycle = new OptimizationJobLifecycle(database.Factory);
        var observedAt = new DateTime(2026, 8, 18, 3, 0, 0, DateTimeKind.Utc);

        await lifecycle.ApplyDispositionAsync(stored.Id, disposition, observedAt);

        var saved = await database.FindAsync(stored.Id);
        saved!.Status.Should().Be(expectedStatus);
        saved.CompletedAt.Should().Be(observedAt);
        saved.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ShutdownAndFailureTransitionsPreserveTheirDistinctEvidence()
    {
        await using var database = await TestDatabase.OpenAsync();
        var stored = await database.AddAsync(new OptimizationJob
        {
            Name = "restart",
            Status = OptimizationJobStatus.Running,
            CompletedAt = DateTime.UtcNow,
            ErrorMessage = "old"
        });
        var lifecycle = new OptimizationJobLifecycle(database.Factory);

        await lifecycle.ReturnToPendingAsync(stored.Id);

        var pending = await database.FindAsync(stored.Id);
        pending!.Status.Should().Be(OptimizationJobStatus.Pending);
        pending.CompletedAt.Should().BeNull();
        pending.ErrorMessage.Should().BeNull();

        await using (var reset = database.Factory.CreateDbContext())
        {
            await reset.OptimizationJobs.Where(job => job.Id == stored.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.Status, OptimizationJobStatus.Running));
        }
        var failedAt = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);
        await lifecycle.MarkFailedAsync(stored.Id, failedAt, "data unavailable");

        var failed = await database.FindAsync(stored.Id);
        failed!.Status.Should().Be(OptimizationJobStatus.Failed);
        failed.CompletedAt.Should().Be(failedAt);
        failed.ErrorMessage.Should().Be("data unavailable");
    }

    [Fact]
    public async Task TerminalWrite_DoesNotOverwriteConcurrentOperatorState()
    {
        await using var database = await TestDatabase.OpenAsync();
        var stored = await database.AddAsync(new OptimizationJob
        {
            Name = "operator-wins",
            Status = OptimizationJobStatus.Cancelled,
            CompletedAt = new DateTime(2026, 8, 18, 3, 30, 0, DateTimeKind.Utc)
        });
        var lifecycle = new OptimizationJobLifecycle(database.Factory);

        await lifecycle.ApplyDispositionAsync(
            stored.Id,
            OptimizationJobExecutionDisposition.Completed,
            new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc));

        var saved = await database.FindAsync(stored.Id);
        saved!.Status.Should().Be(OptimizationJobStatus.Cancelled);
        saved.CompletedAt.Should().Be(stored.CompletedAt);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, Factory factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public Factory Factory { get; }

        public static async Task<TestDatabase> OpenAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var factory = new Factory(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection).Options);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, factory);
        }

        public async Task<OptimizationJob> AddAsync(OptimizationJob job)
        {
            await using var db = Factory.CreateDbContext();
            db.OptimizationJobs.Add(job);
            await db.SaveChangesAsync();
            return job;
        }

        public async Task<OptimizationJob?> FindAsync(int id)
        {
            await using var db = Factory.CreateDbContext();
            return await db.OptimizationJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == id);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobControlServiceTests
{
    [Theory]
    [InlineData(
        OptimizationJobControlState.Pending,
        OptimizationJobControlCommand.Pause,
        OptimizationJobControlState.Paused)]
    [InlineData(
        OptimizationJobControlState.Running,
        OptimizationJobControlCommand.Pause,
        OptimizationJobControlState.Paused)]
    [InlineData(
        OptimizationJobControlState.Paused,
        OptimizationJobControlCommand.Resume,
        OptimizationJobControlState.Pending)]
    [InlineData(
        OptimizationJobControlState.Paused,
        OptimizationJobControlCommand.Cancel,
        OptimizationJobControlState.Cancelled)]
    public void Resolve_ProducesOnlySupportedUserTransitions(
        OptimizationJobControlState current,
        OptimizationJobControlCommand command,
        OptimizationJobControlState expected)
    {
        var observedAt = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

        var transition = OptimizationJobControlPolicy.Resolve(current, command, observedAt);

        transition.Should().NotBeNull();
        transition!.From.Should().Be(current);
        transition.To.Should().Be(expected);
        transition.CompletedAt.Should().Be(
            command == OptimizationJobControlCommand.Cancel ? observedAt : null);
    }

    [Theory]
    [InlineData(OptimizationJobControlState.Completed)]
    [InlineData(OptimizationJobControlState.Cancelled)]
    [InlineData(OptimizationJobControlState.Failed)]
    public void Resolve_RejectsTerminalJobControl(OptimizationJobControlState state)
    {
        var observedAt = new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc);

        OptimizationJobControlPolicy.Resolve(
                state, OptimizationJobControlCommand.Cancel, observedAt)
            .Should().BeNull();
        OptimizationJobControlPolicy.Resolve(
                state, OptimizationJobControlCommand.Pause, observedAt)
            .Should().BeNull();
        OptimizationJobControlPolicy.Resolve(
                state, OptimizationJobControlCommand.Resume, observedAt)
            .Should().BeNull();
    }

    [Fact]
    public async Task ApplyAsync_ReportsConcurrentStateChangeInsteadOfOverwritingIt()
    {
        var store = new Mock<IOptimizationJobControlStore>();
        store.SetupSequence(value => value.GetStateAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OptimizationJobControlState.Running)
            .ReturnsAsync(OptimizationJobControlState.Cancelled);
        store.Setup(value => value.TryTransitionAsync(
                7,
                It.IsAny<OptimizationJobStateTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new OptimizationJobControlService(store.Object);

        var result = await service.ApplyAsync(
            7,
            OptimizationJobControlCommand.Pause,
            new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc));

        result.Outcome.Should().Be(OptimizationJobControlOutcome.ConcurrentChange);
        result.State.Should().Be(OptimizationJobControlState.Cancelled);
    }

    [Fact]
    public async Task SqliteStore_AllowsOnlyOneConditionalTransitionFromTheSameState()
    {
        await using var database = await OpenSharedDatabaseAsync();
        var factory = database.Factory;
        var repository = new OptimizationRepository(factory);
        var job = await repository.CreateJobAsync(new OptimizationJob
        {
            Name = "control-race",
            Status = OptimizationJobStatus.Running
        });
        var store = new OptimizationJobControlStore(factory);
        var observedAt = new DateTime(2026, 8, 18, 5, 0, 0, DateTimeKind.Utc);

        var outcomes = await Task.WhenAll(
            store.TryTransitionAsync(
                job.Id,
                new OptimizationJobStateTransition(
                    OptimizationJobControlState.Running,
                    OptimizationJobControlState.Paused,
                    null)),
            store.TryTransitionAsync(
                job.Id,
                new OptimizationJobStateTransition(
                    OptimizationJobControlState.Running,
                    OptimizationJobControlState.Cancelled,
                    observedAt)));

        outcomes.Count(success => success).Should().Be(1);
        var state = await store.GetStateAsync(job.Id);
        state.Should().BeOneOf(
            OptimizationJobControlState.Paused,
            OptimizationJobControlState.Cancelled);
    }

    [Fact]
    public async Task SqliteStore_RecoveryRequeuesOnlyRunningJobsAndRewindsTheirChunk()
    {
        await using var database = await OpenSharedDatabaseAsync();
        var repository = new OptimizationRepository(database.Factory);
        var running = await repository.CreateJobAsync(new OptimizationJob
        {
            Name = "recover-running",
            Status = OptimizationJobStatus.Running,
            CurrentChunkIndex = 3
        });
        var paused = await repository.CreateJobAsync(new OptimizationJob
        {
            Name = "preserve-paused",
            Status = OptimizationJobStatus.Paused,
            CurrentChunkIndex = 2
        });
        var store = new OptimizationJobControlStore(database.Factory);

        var recovered = await store.RecoverInterruptedAsync();

        recovered.Should().Be(1);
        var savedRunning = await repository.GetJobSummaryAsync(running.Id);
        savedRunning!.Status.Should().Be(OptimizationJobStatus.Pending);
        savedRunning.CurrentChunkIndex.Should().Be(2);
        var savedPaused = await repository.GetJobSummaryAsync(paused.Id);
        savedPaused!.Status.Should().Be(OptimizationJobStatus.Paused);
        savedPaused.CurrentChunkIndex.Should().Be(2);
    }

    private static async Task<SharedDatabase> OpenSharedDatabaseAsync()
    {
        var name = $"optimization-control-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={name};Mode=Memory;Cache=Shared";
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

    private sealed class SharedDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;

        public SharedDatabase(SqliteConnection keeper, TestDbContextFactory factory)
        {
            _keeper = keeper;
            Factory = factory;
        }

        public TestDbContextFactory Factory { get; }

        public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

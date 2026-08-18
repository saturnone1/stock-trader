using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobManagementServiceTests
{
    [Fact]
    public void CombinationCount_MultipliesEveryConfiguredAxis()
    {
        var parameters = new OptimizeParams
        {
            AtrStopMultiplier = new ParamRange { Values = [1m, 2m] },
            EntryLogicOptions = ["AND", "OR"],
            RuleParamOverrides =
            [
                new RuleParamRange { Values = [1m, 2m, 3m] }
            ],
            RuleFieldOverrides =
            [
                new RuleFieldRange
                {
                    NumericValues = [1m],
                    StringValues = ["a", "b"]
                }
            ]
        };

        OptimizationCombinationCountPolicy.Calculate(parameters).Should().Be(36);
    }

    [Fact]
    public void CombinationCount_CapsBeforeLongOverflow()
    {
        var thousandValues = Enumerable.Range(1, 1_000)
            .Select(value => (decimal)value)
            .ToList();
        var parameters = new OptimizeParams
        {
            AtrStopMultiplier = new ParamRange { Values = thousandValues },
            AtrTargetMultiplier = new ParamRange { Values = thousandValues },
            MaxHoldingBars = new ParamRange { Values = thousandValues },
            TrailingAtr = new ParamRange { Values = thousandValues }
        };

        OptimizationCombinationCountPolicy.Calculate(parameters)
            .Should().Be(OptimizationCombinationCountPolicy.MaximumReportedCombinations);
    }

    [Fact]
    public async Task CreateAsync_AppliesDefaultsAndUsesTheInjectedClock()
    {
        OptimizationJobRecord? captured = null;
        var store = new Mock<IOptimizationJobManagementStore>();
        store.Setup(value => value.CreateAsync(
                It.IsAny<OptimizationJobRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<OptimizationJobRecord, CancellationToken>(
                (job, _) => captured = job)
            .ReturnsAsync((OptimizationJobRecord job, CancellationToken _) =>
                job with { Id = 17 });
        var now = new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);
        var service = new OptimizationJobManagementService(
            store.Object,
            new FixedTimeProvider(now));

        var result = await service.CreateAsync(new CreateOptimizationJobCommand(
            "  research  ",
            Priority: 3,
            ChunkSize: 0,
            MaxDurationHours: null,
            MaxTestedCombinations: null,
            TopResultsToKeep: 0,
            RankBy: "",
            ContinuousMode: false,
            AutoApplyBestResult: true,
            AutoApplyMinTrades: 0,
            Request: new OptimizeRequest()));

        result.Outcome.Should().Be(OptimizationJobCreateOutcome.Created);
        result.Job!.Id.Should().Be(17);
        captured!.Name.Should().Be("research");
        captured.State.Should().Be(OptimizationJobControlState.Pending);
        captured.ChunkSize.Should().Be(200);
        captured.TopResultsToKeep.Should().Be(50);
        captured.RankBy.Should().Be("sortinoRatio");
        captured.AutoApplyMinTrades.Should().Be(10);
        captured.CreatedAt.Should().Be(now.UtcDateTime);
        captured.RequestJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FindAsync_ProjectsProgressAndRemainingTimeFromOneObservation()
    {
        var now = new DateTimeOffset(2026, 8, 18, 7, 0, 0, TimeSpan.Zero);
        var stored = new OptimizationJobRecord
        {
            Id = 3,
            Name = "projection",
            State = OptimizationJobControlState.Running,
            TotalCombinations = 100,
            TestedCombinations = 25,
            CreatedAt = now.UtcDateTime.AddMinutes(-5),
            StartedAt = now.UtcDateTime.AddSeconds(-100)
        };
        var store = new Mock<IOptimizationJobManagementStore>();
        store.Setup(value => value.FindAsync(3, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var service = new OptimizationJobManagementService(
            store.Object,
            new FixedTimeProvider(now));

        var result = await service.FindAsync(3);

        result!.Summary.ProgressPercent.Should().Be(25m);
        result.ElapsedSeconds.Should().Be(100d);
        result.EstimatedRemainingSeconds.Should().Be(300d);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotOverwriteAConcurrentStateChange()
    {
        var completed = new OptimizationJobRecord
        {
            Id = 9,
            State = OptimizationJobControlState.Completed
        };
        var running = completed with { State = OptimizationJobControlState.Running };
        var store = new Mock<IOptimizationJobManagementStore>();
        store.SetupSequence(value => value.FindAsync(
                9, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed)
            .ReturnsAsync(running);
        store.Setup(value => value.TryDeleteAsync(
                9,
                OptimizationJobControlState.Completed,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new OptimizationJobManagementService(
            store.Object,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await service.DeleteAsync(9);

        result.Outcome.Should().Be(OptimizationJobDeleteOutcome.ConcurrentChange);
        result.State.Should().Be(OptimizationJobControlState.Running);
    }

    [Fact]
    public async Task SqliteStore_ReturnsResultIdentityAndCascadesConditionalDelete()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var setup = factory.CreateDbContext())
        {
            await setup.Database.EnsureCreatedAsync();
            var job = new OptimizationJob
            {
                Name = "stored-result-id",
                Status = OptimizationJobStatus.Completed
            };
            setup.OptimizationJobs.Add(job);
            await setup.SaveChangesAsync();
            setup.OptimizationResults.Add(new OptimizationResult
            {
                JobId = job.Id,
                Rank = 1,
                ParamsJson = "not-json",
                TotalReturn = 7m
            });
            await setup.SaveChangesAsync();
        }
        var store = new OptimizationJobManagementStore(factory);

        (await store.UpdateSettingsAsync(1, true, 25)).Should().BeTrue();

        var loaded = await store.FindAsync(1, 10);

        loaded.Should().NotBeNull();
        loaded!.AutoApplyBestResult.Should().BeTrue();
        loaded.AutoApplyMinTrades.Should().Be(25);
        loaded.Results.Should().ContainSingle();
        loaded.Results[0].Id.Should().BeGreaterThan(0);
        loaded.Results[0].Params.Should().NotBeNull();
        (await store.TryDeleteAsync(
            loaded.Id,
            OptimizationJobControlState.Completed)).Should().BeTrue();
        await using var assertion = factory.CreateDbContext();
        (await assertion.OptimizationJobs.CountAsync()).Should().Be(0);
        (await assertion.OptimizationResults.CountAsync()).Should().Be(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Api;
using StockTrader.BackgroundServices;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobExecutionTests
{
    [Fact]
    public async Task UpdateJobProgressAsync_PreservesExternalStatusChanges()
    {
        var factory = CreateDbFactory();
        var repo = new OptimizationRepository(factory);

        var created = await repo.CreateJobAsync(new OptimizationJob
        {
            Name = "resume-test",
            Status = OptimizationJobStatus.Paused,
            CompletedAt = new DateTime(2026, 4, 29, 12, 0, 0, DateTimeKind.Utc),
            ErrorMessage = "paused externally"
        });

        var progressAt = new DateTime(2026, 4, 29, 12, 5, 0, DateTimeKind.Utc);
        await repo.UpdateJobProgressAsync(created.Id, 320, 4, progressAt, 1200);

        var saved = await repo.GetJobSummaryAsync(created.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(OptimizationJobStatus.Paused);
        saved.CompletedAt.Should().Be(new DateTime(2026, 4, 29, 12, 0, 0, DateTimeKind.Utc));
        saved.ErrorMessage.Should().Be("paused externally");
        saved.TestedCombinations.Should().Be(320);
        saved.CurrentChunkIndex.Should().Be(4);
        saved.LastProgressAt.Should().Be(progressAt);
        saved.TotalCombinations.Should().Be(1200);
    }

    [Fact]
    public void CalculateStage1StartChunk_SkipsCompletedStage1AfterRestart()
    {
        var startChunk = OptimizationJobExecutor.CalculateStage1StartChunk(
            testedCombinations: 600,
            stage1CombinationCount: 600,
            persistedChunkIndex: 2,
            totalChunks: 3);

        startChunk.Should().Be(3);
    }

    [Theory]
    [InlineData(600, 600, 200, 0)]
    [InlineData(800, 600, 200, 1)]
    [InlineData(873, 600, 200, 2)]
    public void CalculateStage2StartChunk_UsesPersistedProgressFromTestedCombinations(
        long testedCombinations,
        int stage1CombinationCount,
        int chunkSize,
        int expectedChunk)
    {
        var startChunk = OptimizationJobExecutor.CalculateStage2StartChunk(
            testedCombinations,
            stage1CombinationCount,
            chunkSize);

        startChunk.Should().Be(expectedChunk);
    }

    [Fact]
    public void BuildStage2CandidatePool_FillsRemainingBudgetFromUntestedCombinations()
    {
        var stage1 = new List<OptimizeParamSnapshot>
        {
            CreateSnapshot(1m),
            CreateSnapshot(2m)
        };
        var preferred = new List<OptimizeParamSnapshot>
        {
            CreateSnapshot(3m)
        };
        var all = new List<OptimizeParamSnapshot>
        {
            CreateSnapshot(1m),
            CreateSnapshot(2m),
            CreateSnapshot(3m),
            CreateSnapshot(4m),
            CreateSnapshot(5m),
            CreateSnapshot(6m)
        };

        var selected = OptimizationJobExecutor.BuildStage2CandidatePool(
            preferred,
            stage1,
            all,
            budget: 4,
            randomSeed: 42);

        selected.Should().HaveCount(4);
        selected.Select(s => s.AtrStopMultiplier).Should().OnlyHaveUniqueItems();
        selected.Select(s => s.AtrStopMultiplier).Should().NotContain([1m, 2m]);
        selected.First().AtrStopMultiplier.Should().Be(3m);
    }

    private static OptimizeParamSnapshot CreateSnapshot(decimal atrStop)
        => new()
        {
            AtrStopMultiplier = atrStop,
            RuleOverrides = new(),
            RuleFieldOverrides = new()
        };

    private static IDbContextFactory<AppDbContext> CreateDbFactory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}

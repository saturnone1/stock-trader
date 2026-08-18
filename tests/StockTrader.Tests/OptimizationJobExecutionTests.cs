using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Api;
using StockTrader.Application.Optimization;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;

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
        var startChunk = OptimizationJobExecutionPolicy.CalculateStage1StartChunk(
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
        var startChunk = OptimizationJobExecutionPolicy.CalculateStage2StartChunk(
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

        var selected = OptimizationJobExecutionPolicy.BuildStage2CandidatePool(
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

    [Fact]
    public void SplitPeriod_ClampsOutOfSampleToHalfAndUsesOneBoundary()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(100);

        var split = OptimizationJobExecutionPolicy.SplitPeriod(from, to, 0.75m);

        split.InSampleTo.Should().Be(from.AddDays(50));
        split.OutOfSampleFrom.Should().Be(split.InSampleTo);
        split.OutOfSampleTo.Should().Be(to);
        split.HasOutOfSample.Should().BeTrue();
    }

    [Fact]
    public void BuildSearchPlan_IsDeterministicAndPreservesSixtyFortyBudget()
    {
        var combinations = Enumerable.Range(1, 20)
            .Select(value => CreateSnapshot(value))
            .ToList();

        var first = OptimizationJobExecutionPolicy.BuildSearchPlan(combinations, 10);
        var resumed = OptimizationJobExecutionPolicy.BuildSearchPlan(combinations, 10);

        first.Stage1Combinations.Should().HaveCount(6);
        first.Stage2Budget.Should().Be(4);
        first.Stage1Combinations.Select(value => value.AtrStopMultiplier)
            .Should().Equal(resumed.Stage1Combinations.Select(value => value.AtrStopMultiplier));
        first.Stage1Combinations.Select(value => value.AtrStopMultiplier)
            .Should().Equal(1m, 4m, 7m, 11m, 14m, 17m);
    }

    [Fact]
    public void HasExceededDuration_UsesInjectedObservationBoundary()
    {
        var startedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        OptimizationJobExecutionPolicy.HasExceededDuration(
                startedAt, startedAt.AddHours(2), 2m)
            .Should().BeFalse("the existing limit is exceeded only after the boundary");
        OptimizationJobExecutionPolicy.HasExceededDuration(
                startedAt, startedAt.AddHours(2).AddTicks(1), 2m)
            .Should().BeTrue();
        OptimizationJobExecutionPolicy.HasExceededDuration(
                startedAt, startedAt.AddYears(1), null)
            .Should().BeFalse();
    }

    [Fact]
    public void ResultProjection_UsesTheSamePercentUnitsForRankingAndOutOfSample()
    {
        var backtest = new BacktestResult
        {
            TotalReturnPercent = 0.125m,
            SortinoRatio = 1.4m,
            SharpeRatio = 1.1m,
            MaxDrawdown = 0.08m,
            OverallWinRate = 0.625m,
            TotalTrades = 24,
            ProfitFactor = 1.8m,
            CalmarRatio = 1.25m,
            AnnualizedReturn = 0.21m
        };
        var parameters = CreateSnapshot(2m);

        var item = OptimizationResultProjection.ToResultItem(parameters, backtest);
        OptimizationResultProjection.ApplyOutOfSample(item, backtest);

        item.TotalReturn.Should().Be(12.5m);
        item.MaxDrawdown.Should().Be(8m);
        item.WinRate.Should().Be(62.5m);
        item.OosTotalReturn.Should().Be(item.TotalReturn);
        item.OosMaxDrawdown.Should().Be(item.MaxDrawdown);
        item.OosWinRate.Should().Be(item.WinRate);
        item.OosSortinoRatio.Should().Be(item.SortinoRatio);
        item.OosTotalTrades.Should().Be(item.TotalTrades);
        item.OosAnnualizedReturn.Should().Be(item.AnnualizedReturn);
    }

    [Fact]
    public void DataPreparationPolicy_DeduplicatesRequestedTimeFramesAndReferenceSymbols()
    {
        var request = new OptimizeRequest
        {
            Symbols = ["TQQQ", "spy"],
            TimeFrame = TimeFrame.Daily,
            OptimizeParams = new OptimizeParams
            {
                TimeFrameOptions =
                [
                    (int)TimeFrame.Daily,
                    (int)TimeFrame.Weekly,
                    (int)TimeFrame.Daily
                ]
            }
        };

        var timeFrames = OptimizationDataPreparationPolicy.ResolveTimeFrames(request);
        var symbols = OptimizationDataPreparationPolicy.ResolveSymbols(
            request, ["SPY", "VIX"]);

        timeFrames.Should().Equal(TimeFrame.Daily, TimeFrame.Weekly);
        symbols.Should().Equal("TQQQ", "spy", "VIX");
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

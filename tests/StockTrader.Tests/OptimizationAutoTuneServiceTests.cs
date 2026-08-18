using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Api;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class OptimizationAutoTuneServiceTests
{
    [Fact]
    public void SelectPromotionCandidate_PrefersPositiveOosCandidate()
    {
        var badOos = Candidate(1, 30m, 1.5m, 40, -5m, -0.2m, 20);
        var goodOos = Candidate(2, 12m, 0.8m, 35, 6m, 0.4m, 18);

        var selected = OptimizationPromotionPolicy.SelectCandidate(
            [badOos, goodOos],
            rankBy: "sortinoRatio",
            minTrades: 10);

        selected.Should().NotBeNull();
        selected!.Id.Should().Be(2);
    }

    [Fact]
    public async Task HandleCompletedJobAsync_AutoAppliesAndRequeuesSameJob()
    {
        var services = new ServiceCollection();
        var connectionString = $"Data Source=autotune-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IOptimizationAutoTuneStore, OptimizationAutoTuneStore>();
        services.AddScoped<ICustomPatternStore, CustomPatternStore>();
        services.AddScoped<CustomPatternManagementService>();
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OptimizationAutoTuneService>();

        await using var provider = services.BuildServiceProvider();
        using var seedScope = provider.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var pattern = new CustomPatternDefinition
        {
            Name = "AutoTune Pattern",
            EntryGroupsJson = ValidEntryGroupsJson(),
            AtrStopMultiplier = 2.0m,
            AtrTargetMultiplier = 3.0m,
            MaxHoldingBars = 10,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.CustomPatterns.Add(pattern);
        await db.SaveChangesAsync();

        var request = new OptimizeRequest
        {
            BasePattern = StrategyVariantFactory.CloneStrategyDocument(pattern.ToStoredStrategy().Document),
            Symbols = ["TQQQ"],
            From = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RankBy = "sortinoRatio",
            MaxCombinations = 500,
            OosPercent = 0.25m
        };

        var job = new OptimizationJob
        {
            Name = "AutoTune Job",
            Status = OptimizationJobStatus.Completed,
            RankBy = "sortinoRatio",
            TopResultsToKeep = 20,
            ContinuousMode = true,
            AutoApplyBestResult = true,
            AutoApplyMinTrades = 10,
            RequestJson = JsonSerializer.Serialize(request),
            CompletedAt = DateTime.UtcNow
        };
        db.OptimizationJobs.Add(job);
        await db.SaveChangesAsync();

        db.OptimizationResults.Add(new OptimizationResult
        {
            JobId = job.Id,
            Rank = 1,
            ParamsJson = JsonSerializer.Serialize(new OptimizeParamSnapshot
            {
                AtrStopMultiplier = 1.25m,
                AtrTargetMultiplier = 4.5m,
                MaxHoldingBars = 18
            }),
            TotalReturn = 18m,
            SortinoRatio = 1.2m,
            TotalTrades = 25,
            OosTotalReturn = 7m,
            OosSortinoRatio = 0.6m,
            OosTotalTrades = 12
        });
        await db.SaveChangesAsync();

        using (var serviceScope = provider.CreateScope())
        {
            var sut = serviceScope.ServiceProvider.GetRequiredService<OptimizationAutoTuneService>();
            await sut.HandleCompletedJobAsync(job.Id);
        }

        using var assertScope = provider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPattern = await assertDb.CustomPatterns.SingleAsync();
        updatedPattern.AtrStopMultiplier.Should().Be(1.25m);
        updatedPattern.AtrTargetMultiplier.Should().Be(4.5m);
        updatedPattern.MaxHoldingBars.Should().Be(18);

        var savedJob = await assertDb.OptimizationJobs.AsNoTracking()
            .SingleOrDefaultAsync(saved => saved.Id == job.Id);
        savedJob.Should().NotBeNull();
        savedJob!.LastAutoAppliedResultId.Should().NotBeNull();
        savedJob.LastAutoApplyMessage.Should().Contain("자동 반영 완료");
        savedJob.AppliedResultCount.Should().Be(1);

        var jobs = await assertDb.OptimizationJobs.AsNoTracking().ToListAsync();
        jobs.Should().HaveCount(1);
        var recycledJob = jobs.Single();
        recycledJob.Id.Should().Be(job.Id);
        recycledJob.Status.Should().Be(OptimizationJobStatus.Pending);
        recycledJob.ContinuousMode.Should().BeTrue();
        recycledJob.AutoApplyBestResult.Should().BeTrue();
        recycledJob.AutoApplyMinTrades.Should().Be(10);
        recycledJob.TestedCombinations.Should().Be(0);
        recycledJob.CurrentChunkIndex.Should().Be(0);
        recycledJob.StartedAt.Should().BeNull();
        recycledJob.CompletedAt.Should().BeNull();

        var recycledResults = await assertDb.OptimizationResults.AsNoTracking()
            .Where(result => result.JobId == job.Id).ToListAsync();
        recycledResults.Should().BeEmpty();

        var nextRequest = OptimizeRequestJsonCodec.Deserialize(recycledJob.RequestJson);
        nextRequest.Should().NotBeNull();
        nextRequest!.BasePattern.StoredStrategyId.Should().Be(pattern.Id);
        nextRequest.BasePattern.AtrStopMultiplier.Should().Be(1.25m);
        nextRequest.BasePattern.AtrTargetMultiplier.Should().Be(4.5m);
        nextRequest.BasePattern.MaxHoldingBars.Should().Be(18);
        nextRequest.To.Should().BeOnOrAfter(request.To);
    }

    [Fact]
    public async Task ApplyResultAsync_ManuallyAppliesSelectedResultAndIncrementsCount()
    {
        var services = new ServiceCollection();
        var connectionString = $"Data Source=autotune-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();

        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IOptimizationAutoTuneStore, OptimizationAutoTuneStore>();
        services.AddScoped<ICustomPatternStore, CustomPatternStore>();
        services.AddScoped<CustomPatternManagementService>();
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OptimizationAutoTuneService>();

        await using var provider = services.BuildServiceProvider();
        using var seedScope = provider.CreateScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var pattern = new CustomPatternDefinition
        {
            Name = "Manual Apply Pattern",
            EntryGroupsJson = ValidEntryGroupsJson(),
            AtrStopMultiplier = 2.0m,
            AtrTargetMultiplier = 3.0m,
            MaxHoldingBars = 10,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.CustomPatterns.Add(pattern);
        await db.SaveChangesAsync();

        var request = new OptimizeRequest
        {
            BasePattern = StrategyVariantFactory.CloneStrategyDocument(pattern.ToStoredStrategy().Document),
            Symbols = ["QQQ"],
            From = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RankBy = "sortinoRatio",
            MaxCombinations = 500
        };

        var job = new OptimizationJob
        {
            Name = "Manual Apply Job",
            Status = OptimizationJobStatus.Running,
            RankBy = "sortinoRatio",
            TopResultsToKeep = 20,
            AutoApplyBestResult = false,
            RequestJson = JsonSerializer.Serialize(request)
        };
        db.OptimizationJobs.Add(job);
        await db.SaveChangesAsync();

        var selectedResult = new OptimizationResult
        {
            JobId = job.Id,
            Rank = 1,
            ParamsJson = JsonSerializer.Serialize(new OptimizeParamSnapshot
            {
                AtrStopMultiplier = 1.5m,
                AtrTargetMultiplier = 4.0m,
                MaxHoldingBars = 14
            }),
            TotalReturn = 10m,
            SortinoRatio = 0.9m,
            TotalTrades = 18
        };
        db.OptimizationResults.Add(selectedResult);
        await db.SaveChangesAsync();

        var invalidResult = new OptimizationResult
        {
            JobId = job.Id,
            Rank = 2,
            ParamsJson = JsonSerializer.Serialize(new OptimizeParamSnapshot
            {
                AtrStopMultiplier = -1m
            }),
            TotalReturn = 20m,
            SortinoRatio = 1.5m,
            TotalTrades = 20
        };
        db.OptimizationResults.Add(invalidResult);
        await db.SaveChangesAsync();

        using var serviceScope = provider.CreateScope();
        var sut = serviceScope.ServiceProvider.GetRequiredService<OptimizationAutoTuneService>();
        var rejected = await sut.ApplyResultAsync(job.Id, invalidResult.Id, isAutoApply: false);

        rejected.Success.Should().BeFalse();
        rejected.Message.Should().Contain("ATR 손절 배수");
        (await db.CustomPatterns.AsNoTracking().SingleAsync()).AtrStopMultiplier.Should().Be(2m);
        (await db.OptimizationJobs.AsNoTracking().SingleAsync(saved => saved.Id == job.Id))
            .AppliedResultCount.Should().Be(0);

        var outcome = await sut.ApplyResultAsync(job.Id, selectedResult.Id, isAutoApply: false);

        outcome.Success.Should().BeTrue();
        outcome.AppliedResultId.Should().Be(selectedResult.Id);
        outcome.AppliedResultCount.Should().Be(1);

        using var assertScope = provider.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var updatedPattern = await assertDb.CustomPatterns.SingleAsync();
        updatedPattern.AtrStopMultiplier.Should().Be(1.5m);
        updatedPattern.AtrTargetMultiplier.Should().Be(4.0m);
        updatedPattern.MaxHoldingBars.Should().Be(14);

        var updatedJob = await assertDb.OptimizationJobs.AsNoTracking()
            .SingleOrDefaultAsync(saved => saved.Id == job.Id);
        updatedJob.Should().NotBeNull();
        updatedJob!.AppliedResultCount.Should().Be(1);
        updatedJob.LastAutoAppliedResultId.Should().Be(selectedResult.Id);
        updatedJob.LastAutoApplyMessage.Should().Contain("수동 반영 완료");
    }

    private static string ValidEntryGroupsJson() => JsonSerializer.Serialize(new[]
    {
        new ConditionGroup
        {
            Rules =
            [
                new EntryRule
                {
                    Indicator = "RSI",
                    Operator = "<=",
                    Value = 30m,
                    Params = new() { ["period"] = 14m }
                }
            ]
        }
    });

    private static OptimizationPromotionCandidate Candidate(
        int id,
        decimal totalReturn,
        decimal sortino,
        int trades,
        decimal? oosReturn,
        decimal? oosSortino,
        int? oosTrades) => new(
            id,
            new OptimizeParamSnapshot(),
            totalReturn,
            sortino,
            SharpeRatio: 0m,
            WinRate: 0m,
            trades,
            ProfitFactor: 0m,
            CalmarRatio: 0m,
            AnnualizedReturn: 0m,
            oosReturn,
            oosSortino,
            OosSharpeRatio: null,
            OosWinRate: null,
            oosTrades,
            OosProfitFactor: null,
            OosCalmarRatio: null,
            OosAnnualizedReturn: null);
}

using FluentAssertions;
using Moq;
using StockTrader.Application.Optimization;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OptimizationJobExecutionStoreTests
{
    [Fact]
    public async Task SaveChunkAsync_MapsResultsAndPersistsTheFollowingCheckpoint()
    {
        var repository = new Mock<IOptimizationRepository>();
        List<OptimizationResult>? captured = null;
        repository.Setup(value => value.CommitChunkAsync(
                7,
                It.IsAny<List<OptimizationResult>>(),
                25,
                "sortinoRatio",
                121,
                4,
                It.IsAny<DateTime>()))
            .Callback<int, List<OptimizationResult>, int, string, long, int, DateTime>(
                (_, results, _, _, _, _, _) => captured = results)
            .Returns(Task.CompletedTask);
        var store = new OptimizationJobExecutionStore(repository.Object);
        var observedAt = new DateTime(2026, 8, 18, 1, 2, 3, DateTimeKind.Utc);
        var parameters = new OptimizeParamSnapshot
        {
            AtrStopMultiplier = 2.5m,
            RuleOverrides = [],
            RuleFieldOverrides = []
        };
        var result = new OptimizeResultItem
        {
            Params = parameters,
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
            7, [result], 120, 121, 4, observedAt, 25, "sortinoRatio");

        captured.Should().ContainSingle();
        var entity = captured![0];
        entity.JobId.Should().Be(7);
        entity.TestedAtCombination.Should().Be(120);
        entity.DiscoveredAt.Should().Be(observedAt);
        entity.TotalReturn.Should().Be(result.TotalReturn);
        entity.SortinoRatio.Should().Be(result.SortinoRatio);
        entity.TotalTrades.Should().Be(result.TotalTrades);
        entity.ParamsJson.Should().Contain("2.5");
        repository.Verify(value => value.CommitChunkAsync(
            7,
            It.IsAny<List<OptimizationResult>>(),
            25,
            "sortinoRatio",
            121,
            4,
            observedAt), Times.Once);
    }

    [Fact]
    public async Task LoadTopCandidatesAsync_SkipsMalformedLegacyParameterJson()
    {
        var repository = new Mock<IOptimizationRepository>();
        repository.Setup(value => value.GetResultsAsync(7, 5))
            .ReturnsAsync(
            [
                new OptimizationResult
                {
                    Id = 11,
                    ParamsJson = "{\"atrStopMultiplier\":2.5}"
                },
                new OptimizationResult { Id = 12, ParamsJson = "not-json" }
            ]);
        var store = new OptimizationJobExecutionStore(repository.Object);

        var candidates = await store.LoadTopCandidatesAsync(7, 5);

        candidates.Should().ContainSingle();
        candidates[0].ResultId.Should().Be(11);
        candidates[0].Parameters.AtrStopMultiplier.Should().Be(2.5m);
    }
}

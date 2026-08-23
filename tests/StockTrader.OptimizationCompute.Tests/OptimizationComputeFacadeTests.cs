using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Optimization.Compute;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.OptimizationCompute.Tests;

public sealed class OptimizationComputeFacadeTests
{
    private static readonly DateTime From =
        new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExecuteAsync_RunsFullSearchFromPreparedInputOnly()
    {
        var input = Input();
        var lease = new OptimizationWorkLease(
            OptimizationWorkerContractCatalog.LeaseVersion,
            "compute-lease",
            12,
            1,
            0,
            From,
            From.AddMinutes(5),
            input)
        {
            Purpose = OptimizationWorkerContractCatalog.OptimizationComputePurpose
        };

        var result = await OptimizationComputeFacade.ExecuteAsync(lease);

        result.InputHash.Should().Be(input.InputHash);
        result.Purpose.Should().Be(OptimizationWorkerContractCatalog.OptimizationComputePurpose);
        result.TotalCombinations.Should().Be(1);
        result.TestedCombinations.Should().Be(1);
        result.Results.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_RejectsValidationOnlyLease()
    {
        var lease = new OptimizationWorkLease(
            OptimizationWorkerContractCatalog.LeaseVersion,
            "validation-lease",
            12,
            1,
            0,
            From,
            From.AddMinutes(5),
            Input());

        var action = () => OptimizationComputeFacade.ExecuteAsync(lease);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported compute purpose*");
    }

    private static OptimizationEvaluationInput Input()
    {
        var strategy = new StrategyDocument
        {
            Name = "compute-test",
            EntryRulesJson = "[{\"indicator\":\"RSI\",\"params\":{\"period\":2},\"operator\":\"<=\",\"value\":30}]",
            AtrStopMultiplier = 2,
            AtrTargetMultiplier = 4,
            MaxHoldingBars = 10
        };
        var requestJson = JsonSerializer.Serialize(new
        {
            basePattern = strategy,
            symbols = new[] { "TQQQ" },
            from = From,
            to = From.AddDays(1),
            initialCapital = 100_000m,
            dataSource = 0,
            timeFrame = 3,
            optimizeParams = new { },
            rankBy = "sortinoRatio",
            maxResults = 10,
            maxCombinations = 50,
            oosPercent = 0m
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bars = new[]
        {
            new OptimizationBar(From, 100, 101, 99, 100, 1_000, 100),
            new OptimizationBar(From.AddDays(1), 99, 102, 98, 101, 1_100, 101)
        };
        var series = new OptimizationPreparedSeries(
            "TQQQ", "Daily", bars, [1, 1], [100, 101], [0, 0], [0, 0], [0, 0]);
        var regimes = new[]
        {
            new OptimizationRegimeSnapshot(
                DateOnly.FromDateTime(From), true, 500, 450, 15, "Bull", From, -1, ""),
            new OptimizationRegimeSnapshot(
                DateOnly.FromDateTime(From.AddDays(1)), true, 501, 450, 15, "Bull", From.AddDays(1), -1, "")
        };
        var risk = new OptimizationRiskSnapshot(1, 3, 5, 2);
        var prepared = new OptimizationPreparedDataSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            OptimizationPreparedDataIdentity.Compute([series], regimes, risk),
            [series], regimes, risk);
        var evidenceSeries = new OptimizationSymbolDataEvidence(
            "TQQQ", "Daily", "Alpaca", "UnitedStates", "SplitsAndDividends",
            "RegularSessionOnly", "us-equities-v1", From, From.AddDays(1),
            From, From.AddDays(1), 2, OptimizationDataCompleteness.Unverified, "bars")
        {
            MarketTimeZoneId = "America/New_York",
            RequiredWarmupBars = 0,
            WarmupCalendarDays = 0
        };
        var evidence = new OptimizationDataEvidenceSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            OptimizationDataEvidenceIdentity.Compute([evidenceSeries]),
            [evidenceSeries]);
        var artifact = StrategyExecutionArtifactFactory.Create(strategy);
        var inputHash = OptimizationEvaluationInputIdentity.Compute(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            requestJson,
            artifact.ContentHash,
            evidence.EvidenceId,
            prepared.DataHash);
        return new OptimizationEvaluationInput(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            inputHash,
            requestJson,
            artifact,
            evidence,
            prepared);
    }
}

using FluentAssertions;
using System.Text.Json;
using StockTrader.Application.Backtesting;
using StockTrader.Application.MarketData;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Tests;

public class OptimizationWorkerContractTests
{
    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StrategyArtifact_IgnoresStorageIdentityButDetectsSemanticChanges()
    {
        var first = StrategyExecutionArtifactFactory.Create(Strategy(11, 20));
        var sameDocumentAtAnotherStorageId = StrategyExecutionArtifactFactory.Create(Strategy(99, 20));
        var changedPeriod = StrategyExecutionArtifactFactory.Create(Strategy(11, 30));

        first.ContentHash.Should().Be(sameDocumentAtAnotherStorageId.ContentHash);
        first.ContentHash.Should().NotBe(changedPeriod.ContentHash);
        JsonSerializer.Deserialize<StrategyDocument>(first.StrategyDocumentJson)!
            .StoredStrategyId.Should().BeNull();
        StrategyExecutionArtifactPolicy.CompatibilityError(first).Should().BeNull();
        StrategyExecutionArtifactPolicy.CompatibilityError(
            first with { EngineSemanticsVersion = "future-engine" })
            .Should().Contain("버전");
    }

    [Fact]
    public void DataEvidence_IsStableAndChangesWhenOneBarChanges()
    {
        var first = OptimizationDataEvidenceFactory.Create(Context(100m));
        var same = OptimizationDataEvidenceFactory.Create(Context(100m));
        var corrected = OptimizationDataEvidenceFactory.Create(Context(101m));

        first.EvidenceId.Should().Be(same.EvidenceId);
        first.EvidenceId.Should().NotBe(corrected.EvidenceId);
        first.Series.Should().ContainSingle();
        first.Series[0].Completeness.Should().Be(OptimizationDataCompleteness.Unverified);
        first.Series[0].CalendarVersion.Should().Be(MarketCalendarVersion.Current);
    }

    [Fact]
    public void EvaluationInput_BindsRequestStrategyAndExactDataEvidence()
    {
        var first = OptimizationEvaluationInputFactory.Create(Context(100m));
        var same = OptimizationEvaluationInputFactory.Create(Context(100m));
        var correctedBar = OptimizationEvaluationInputFactory.Create(Context(101m));

        first.InputHash.Should().Be(same.InputHash);
        first.InputHash.Should().NotBe(correctedBar.InputHash);
        first.ContractVersion.Should().Be(OptimizationWorkerContractCatalog.EvaluationInputVersion);
        first.Strategy.ContentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResultAcceptance_FailsClosedForStaleCancelledExpiredOrMutatedWork()
    {
        var input = OptimizationEvaluationInputFactory.Create(Context(100m));
        var leasedAt = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);
        var lease = new OptimizationWorkLease(
            OptimizationWorkerContractCatalog.LeaseVersion,
            "lease-7-2",
            7,
            2,
            4,
            leasedAt,
            leasedAt.AddMinutes(5),
            input);
        const string resultJson = "{\"tested\":3}";
        var submission = new OptimizationWorkerResultSubmission(
            OptimizationWorkerContractCatalog.ResultVersion,
            "submission-1",
            lease.LeaseId,
            lease.JobId,
            lease.LeaseGeneration,
            lease.CancellationGeneration,
            input.InputHash,
            CanonicalJsonHash.Compute(resultJson),
            resultJson,
            leasedAt.AddMinutes(1));

        OptimizationLeaseCompatibilityPolicy.Error(lease).Should().BeNull();
        OptimizationLeaseCompatibilityPolicy.Error(
            lease with { Input = input with { InputHash = "DIFFERENT" } })
            .Should().Be("input-hash-mismatch");
        Evaluate(lease, submission, 4, false, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.Accepted);
        Evaluate(lease, submission, 4, true, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.Duplicate);
        Evaluate(lease, submission with { LeaseGeneration = 1 }, 4, false, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.StaleLease);
        Evaluate(lease, submission, 5, false, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.CancelledGeneration);
        Evaluate(lease, submission with { InputHash = "DIFFERENT" }, 4, false, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.InputMismatch);
        Evaluate(lease, submission, 4, false, leasedAt.AddMinutes(6))
            .Should().Be(OptimizationResultAcceptance.LeaseExpired);
        Evaluate(lease, submission with { ResultJson = "{}" }, 4, false, leasedAt.AddMinutes(2))
            .Should().Be(OptimizationResultAcceptance.ResultHashMismatch);
    }

    private static OptimizationResultAcceptance Evaluate(
        OptimizationWorkLease lease,
        OptimizationWorkerResultSubmission submission,
        long cancellationGeneration,
        bool duplicate,
        DateTime observedAt) => OptimizationResultAcceptancePolicy.Evaluate(
            lease,
            submission,
            cancellationGeneration,
            duplicate,
            observedAt);

    private static StrategyDocument Strategy(int storedId, int period) => new()
    {
        StoredStrategyId = storedId,
        Name = "worker-contract",
        EntryRulesJson = JsonSerializer.Serialize(new[]
        {
            new { indicator = "RSI", @params = new { period }, @operator = "<=", value = 30 }
        }),
        AtrStopMultiplier = 2m,
        AtrTargetMultiplier = 4m,
        MaxHoldingBars = 20
    };

    private static OptimizationEvaluationContext Context(decimal secondClose)
    {
        var bars = new[]
        {
            Bar(From, 100m),
            Bar(From.AddDays(1), secondClose)
        };
        var prepared = new PreparedSymbolData(
            bars,
            [1m, 1m],
            bars.Select(item => item.Close).ToArray(),
            [0m, 0m],
            [0m, 0m],
            [0m, 0m],
            bars.Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(item => item.Timestamp, item => item.index));
        IReadOnlyDictionary<string, PreparedSymbolData> symbols =
            new Dictionary<string, PreparedSymbolData> { ["TQQQ"] = prepared };
        IReadOnlyDictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>> byTimeFrame =
            new Dictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>>
            {
                [TimeFrame.Daily] = symbols
            };
        var request = new OptimizeRequest
        {
            BasePattern = Strategy(11, 20),
            Symbols = ["TQQQ"],
            From = From,
            To = To,
            DataSource = DataSource.Alpaca,
            TimeFrame = TimeFrame.Daily
        };
        var evidence = MarketDataEvidence.Create(
            DataSource.Alpaca,
            TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly,
            300,
            200);
        return new OptimizationEvaluationContext(
            request,
            byTimeFrame,
            symbols,
            [],
            new OptimizationRiskParameters(1m, 3m, 5, 2),
            new Dictionary<TimeFrame, MarketDataEvidence> { [TimeFrame.Daily] = evidence },
            evidence);
    }

    private static OhlcvBar Bar(DateTime timestamp, decimal close) => new()
    {
        Symbol = "TQQQ",
        Timestamp = timestamp,
        TimeFrame = TimeFrame.Daily,
        Open = close - 1,
        High = close + 1,
        Low = close - 2,
        Close = close,
        Volume = 1_000,
        Vwap = close - 0.5m
    };
}

using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Domain.MarketData;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.Execution;

namespace StockTrader.Tests;

public sealed class TradingPositionEvaluatorTests
{
    [Fact]
    public void CompletedWindowExcludesTheActiveUsDailyBar()
    {
        var beforeClose = TradingCompletedBarPolicy.Resolve(
            new DateTime(2026, 8, 27, 17, 0, 0, DateTimeKind.Utc), "Alpaca"); // 13:00 ET
        var afterClose = TradingCompletedBarPolicy.Resolve(
            new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc), "Alpaca"); // 17:00 ET

        beforeClose.ExpectedLastSessionDate.Should().Be(new DateOnly(2026, 8, 26));
        afterClose.ExpectedLastSessionDate.Should().Be(new DateOnly(2026, 8, 27));
        beforeClose.CompletedThroughUtc.Should().BeBefore(afterClose.CompletedThroughUtc);
    }

    [Fact]
    public void CompletedBarStopIsEvaluatedConservativelyInsideTradingCore()
    {
        var bars = new[]
        {
            Bar(new DateTime(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc), 100m, 103m, 99m, 102m),
            Bar(new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc), 94m, 101m, 90m, 98m),
            Bar(new DateTime(2026, 8, 28, 4, 0, 0, DateTimeKind.Utc), 99m, 104m, 98m, 103m),
        };
        var response = Evidence(bars);
        var artifact = Artifact();
        var position = new TradingPositionProjection(
            "position:1", "signal:1", "account:1", "TQQQ", "ETF", 10, 10,
            100m, 98m, 95m, 120m, "Breakout", null,
            bars[0].TimestampUtc, null, null, 103m, 2m, 5m,
            false, false, false, null, null, null, false, null, null, null, [],
            new TradingPositionExecutionContext(artifact, response.Evidence));

        var result = TradingPositionEvaluator.Evaluate(
            position, response, null, 100_000m, 10);

        result.Action.Should().Be(TradingPositionActionKinds.FullExit);
        result.Quantity.Should().Be(10);
        result.Reason.Should().Be("손절");
        result.StopLossPrice.Should().Be(95m);
        result.EvaluatedThroughBarUtc.Should().Be(bars[1].TimestampUtc);
    }

    [Fact]
    public void PositionManagementSnapshotParticipatesInArtifactIdentity()
    {
        var artifact = Artifact();
        var changed = artifact with
        {
            PositionManagement = artifact.PositionManagement! with
            {
                ExitPolicy = artifact.PositionManagement!.ExitPolicy with { MaxHoldingBars = 99 },
            },
        };

        TradingExecutionArtifactPolicy.Error(artifact).Should().BeNull();
        TradingExecutionArtifactPolicy.Error(changed)
            .Should().Be("execution-artifact-hash-mismatch");
    }

    private static TradingStrategyExecutionArtifact Artifact()
    {
        var management = new TradingPositionManagementArtifact(
            new TradingLongPositionPolicy(
                20, true, 2.5m, 1m, true, 2m, true, true,
                1.5m, "손절", "트레일링 손절"),
            2, null, null);
        var hash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, "{}",
            MarketCalendarVersion.Current, management);
        return new TradingStrategyExecutionArtifact(
            TradingCoreContractVersions.Current, hash,
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, "{}", hash,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
            MarketCalendarVersion.Current, true, true, management);
    }

    private static MarketDataExecutionWindowResponse Evidence(IReadOnlyList<MarketDataBar> bars)
    {
        var hash = MarketDataContractHash.Content(bars);
        const long revision = 7;
        var evidence = new MarketDataEvidenceContract(
            MarketDataContractVersions.Current,
            MarketDataContractHash.Evidence(
                "Alpaca", "TQQQ", "Daily", "Raw",
                MarketCalendarVersion.Current, revision, hash),
            "Alpaca", "TQQQ", "Daily", "Raw", "미국",
            MarketCalendarVersion.Current, bars[0].TimestampUtc, bars[^1].TimestampUtc,
            bars[0].TimestampUtc, bars[^1].TimestampUtc, revision, true, hash);
        return new MarketDataExecutionWindowResponse(evidence, bars, false);
    }

    private static MarketDataBar Bar(
        DateTime timestamp, decimal open, decimal high, decimal low, decimal close) =>
        new("TQQQ", "Daily", timestamp, open, high, low, close, 1_000_000, null);
}

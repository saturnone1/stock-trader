using System.Text.Json;
using FluentAssertions;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.Broker;
using StockTrader.TradingCoreService;
using Microsoft.FSharp.Core;

namespace StockTrader.Tests;

public sealed class TradingCoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"trading-core-{Guid.NewGuid():N}");

    [Fact]
    public void DurableEntryAndExitLifecycleIsIdempotentAndAuditable()
    {
        Directory.CreateDirectory(_root);
        var config = new ServiceConfig(
            Path.Combine(_root, "core.db"), new string('x', 32), "unused", "unused", "unused",
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            TradingAuthorityMode.Projection);
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        var now = DateTime.UtcNow;
        var snapshot = EmptySnapshot(now);
        operations.Import(snapshot).Should().BeFalse();
        operations.Import(snapshot).Should().BeTrue();

        var accountConfiguration = AccountConfiguration(now);
        operations.ApplyAccountConfiguration(accountConfiguration)
            .AlreadyApplied.Should().BeFalse();
        operations.ApplyAccountConfiguration(accountConfiguration)
            .AlreadyApplied.Should().BeTrue();
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Shadow, 2, "shadow", now,
            snapshot.SnapshotId, string.Empty, null, 0));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Remote, 3, "remote", now,
            snapshot.SnapshotId, "broker-state", now, 0));

        var entry = EntryIntent(now, operations.Status());
        var accepted = operations.AcceptEntry(entry);
        accepted.Status.Should().Be(TradingCommandStatuses.PendingBrokerSubmission);
        operations.AcceptEntry(entry).AlreadyAccepted.Should().BeTrue();
        Some(operations.ClaimEntry()).Envelope.CommandId
            .Should().Be(entry.Envelope.CommandId);
        operations.RecordBrokerEvidence(entry.Envelope.CommandId, new BrokerOrderEvidence(
            "order-entry", "client-entry", "AAPL", "Buy", 10, 10, null, 101m,
            "Filled", "Market", now, now)).Should().BeTrue();

        var portfolio = operations.Portfolio();
        var position = portfolio.Positions.Should().ContainSingle().Subject;
        position.ExecutionContext.Should().NotBeNull();
        position.Quantity.Should().Be(10);
        Some(operations.CommandStatus(entry.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.Completed);

        var exitAt = DateTime.UtcNow;
        var exit = PositionCommand(exitAt, operations.Status(), position);
        operations.AcceptPosition(exit).Status
            .Should().Be(TradingCommandStatuses.PendingBrokerSubmission);
        Some(operations.ClaimPosition()).PositionId
            .Should().Be(position.PositionId);
        operations.RecordPositionBrokerEvidence(exit.Envelope.CommandId, new BrokerOrderEvidence(
            "order-exit", "client-exit", "AAPL", "Sell", 10, 10, null, 110m,
            "Filled", "Market", exitAt, exitAt)).Should().BeTrue();

        portfolio = operations.Portfolio();
        portfolio.Positions.Single().Quantity.Should().Be(0);
        portfolio.Positions.Single().ClosedAtUtc.Should().NotBeNull();
        portfolio.Trades.Should().ContainSingle()
            .Which.PnL.Should().Be(90m);
        Some(operations.CommandStatus(exit.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.Completed);
    }

    private static TradingStateSnapshot EmptySnapshot(DateTime now)
    {
        var value = new TradingStateSnapshot(1, string.Empty, 1, now,
            [], [], [], [], new TradingRiskProjection(0, 0, 0, false, now));
        return value with { SnapshotId = TradingCoreIdentity.Snapshot(value) };
    }

    private static TradingAccountConfigurationSet AccountConfiguration(DateTime now)
    {
        var value = new TradingAccountConfigurationSet(1, 1, string.Empty, now,
            [new TradingAccountConfiguration("1", "Alpaca", "Paper", true, true, "key", "secret")],
            new TradingRiskConfiguration(0.01m, 0.03m, 7, 2));
        return value with { ConfigurationHash = TradingCoreIdentity.AccountConfiguration(value) };
    }

    private static TradingEntryIntent EntryIntent(DateTime now, TradingCoreStatus status)
    {
        var evidence = Evidence(now);
        const string settings = "{\"patternConfiguration\":{},\"exitPolicy\":{}}";
        var artifactHash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings,
            evidence.CalendarVersion);
        var artifact = new TradingStrategyExecutionArtifact(1, artifactHash,
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings,
            artifactHash, OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion, evidence.CalendarVersion,
            true, true);
        var envelope = new TradingCommandEnvelope(1, "entry-command", TradingCommandKinds.AcceptEntry,
            string.Empty, "correlation", null, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes(5));
        var intent = new TradingEntryIntent(envelope, "signal-1", "1", "AAPL", "Technology",
            "Breakout", null, 100m, 95m, 120m, 10, 0.1m, artifact, evidence);
        return intent with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.EntryPayload(intent) }
        };
    }

    private static TradingPositionCommand PositionCommand(
        DateTime now, TradingCoreStatus status, TradingPositionProjection position)
    {
        var envelope = new TradingCommandEnvelope(1, "exit-command", TradingCommandKinds.ClosePosition,
            string.Empty, "correlation", null, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes(5));
        var command = new TradingPositionCommand(envelope, position.PositionId,
            TradingPositionActionKinds.FullExit, position.Quantity, "target-exit",
            position.ExecutionContext!.ExecutionArtifact.ArtifactId, Evidence(now));
        return command with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.PositionPayload(command) }
        };
    }

    private static MarketDataEvidenceContract Evidence(DateTime now)
    {
        const string contentHash = "content";
        var evidenceId = MarketDataContractHash.Evidence(
            "Yahoo", "AAPL", "Daily", "Raw", "market-calendar-v1", 1, contentHash);
        return new MarketDataEvidenceContract(1, evidenceId, "Yahoo", "AAPL", "Daily", "Raw",
            "US", "market-calendar-v1", now.AddDays(-30), now, now.AddDays(-30), now,
            1, true, contentHash);
    }

    private static T Some<T>(FSharpOption<T>? value)
    {
        value.Should().NotBeNull();
        return value!.Value;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

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
    public void ProjectionPortfolioReadsImportedFinancialRowsInsteadOfEmptyCanonicalTables()
    {
        Directory.CreateDirectory(_root);
        var config = new ServiceConfig(
            Path.Combine(_root, "projection.db"), new string('x', 32), "unused", "unused", "unused",
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            TradingAuthorityMode.Projection);
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        var now = DateTime.UtcNow;
        var recommendation = new TradingRecommendationProjection(
            "recommendation-1", "signal-projection", "TQQQ", "Breakout", null, now,
            100m, 95m, 115m, 5, 0.3m, "AutoOrder", false, null, null, null, null);
        var trade = new TradingTradeProjection(
            "trade-1", "signal-closed", "AAPL", "Breakout", null,
            100m, 110m, 2, now.AddDays(-2), now.AddDays(-1), 20m, 10m, "target");
        var value = new TradingStateSnapshot(1, string.Empty, 1, now,
            [], [recommendation], [], [trade],
            new TradingRiskProjection(-12m, -0.12m, 0, false, now));
        var snapshot = value with { SnapshotId = TradingCoreIdentity.Snapshot(value) };

        operations.Import(snapshot).Should().BeFalse();
        var portfolio = operations.Portfolio();
        portfolio.Recommendations.Should().ContainSingle().Which.Symbol.Should().Be("TQQQ");
        portfolio.Trades.Should().ContainSingle().Which.PnL.Should().Be(20m);
        portfolio.Risk.DailyPnL.Should().Be(-12m);
        portfolio.Accounts.Should().BeEmpty();
    }

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
        operations.Portfolio().Risk.DailyPnL.Should().Be(-12m);

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

        var observation = RecommendationObservation(now, operations.Status());
        operations.RecordRecommendation(observation).AlreadyAccepted.Should().BeFalse();
        operations.RecordRecommendation(observation).AlreadyAccepted.Should().BeTrue();
        operations.Portfolio().Recommendations.Should().ContainSingle(value =>
            value.SourceSignalId == "signal-alert" && value.Mode == "AlertOnly");

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

        var stateUpdate = PositionStateUpdate(now, operations.Status(), position);
        operations.ApplyPositionState(stateUpdate).AlreadyAccepted.Should().BeFalse();
        operations.ApplyPositionState(stateUpdate).AlreadyAccepted.Should().BeTrue();
        var updatedPolicyPosition = operations.Portfolio().Positions.Single();
        updatedPolicyPosition.HighSinceEntry.Should().Be(105m);
        updatedPolicyPosition.StopLossPrice.Should().Be(96m);
        updatedPolicyPosition.BreakevenApplied.Should().BeTrue();

        var exitAt = DateTime.UtcNow;
        var exit = PositionCommand(exitAt, operations.Status(), position);
        operations.AcceptPosition(exit).Status
            .Should().Be(TradingCommandStatuses.PendingBrokerSubmission);
        operations.Portfolio().Positions.Single().ExecutionRequestedAtUtc.Should().Be(exitAt);
        Some(operations.LatestPositionCommand(position.PositionId)).CommandId
            .Should().Be(exit.Envelope.CommandId);
        Some(operations.ClaimPosition()).PositionId
            .Should().Be(position.PositionId);
        operations.RecordPositionBrokerEvidence(exit.Envelope.CommandId, new BrokerOrderEvidence(
            "order-exit", "client-exit", "AAPL", "Sell", 10, 10, null, 110m,
            "Filled", "Market", exitAt, exitAt)).Should().BeTrue();

        portfolio = operations.Portfolio();
        portfolio.Positions.Single().Quantity.Should().Be(0);
        portfolio.Positions.Single().ClosedAtUtc.Should().NotBeNull();
        portfolio.Positions.Single().ExecutionRequestedAtUtc.Should().BeNull();
        portfolio.Trades.Should().ContainSingle()
            .Which.PnL.Should().Be(90m);
        Some(operations.CommandStatus(exit.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.Completed);

        operations.SyncBrokerPortfolio("1", new BrokerAccountEvidence(
            "1", 10_100m, 10_000m, 5_000m, 10_000m, false, exitAt),
            Array.Empty<BrokerPositionEvidence>(), 0.03m);
        portfolio = operations.Portfolio();
        portfolio.Accounts.Should().ContainSingle().Which.DailyPnL.Should().Be(100m);
        portfolio.Risk.DailyPnL.Should().Be(100m);

        operations.SyncBrokerPortfolio("1", new BrokerAccountEvidence(
            "1", 10_100m, 10_000m, 5_000m, 10_000m, false, exitAt),
            new[] { new BrokerPositionEvidence("MSFT", 1, 100m, 101m) }, 0.03m);
        operations.Status().LastError.Should().Be("broker-canonical-portfolio-divergence");
        operations.Portfolio().Risk.IsTradingHalted.Should().BeTrue();
        operations.SyncBrokerPortfolio("1", new BrokerAccountEvidence(
            "1", 10_100m, 10_000m, 5_000m, 10_000m, false, exitAt),
            Array.Empty<BrokerPositionEvidence>(), 0.03m);
        operations.Status().LastError.Should().BeNull();
    }

    [Fact]
    public void RejectedBrokerEvidenceReleasesEntryAndPositionClaims()
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
        operations.Import(snapshot);
        operations.ApplyAccountConfiguration(AccountConfiguration(now));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Shadow, 2, "shadow", now,
            snapshot.SnapshotId, string.Empty, null, 0));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Remote, 3, "remote", now,
            snapshot.SnapshotId, "broker-state", now, 0));

        var entry = EntryIntent(now, operations.Status());
        operations.AcceptEntry(entry);
        operations.ClaimEntry().Should().NotBeNull();
        operations.RecordBrokerEvidence(entry.Envelope.CommandId, new BrokerOrderEvidence(
            "rejected-entry", "client-rejected-entry", "AAPL", "Buy", 10, 0, null, null,
            "Rejected", "Market", now, null)).Should().BeTrue();
        var recommendation = operations.Portfolio().Recommendations.Single(value =>
            value.SourceSignalId == entry.SourceSignalId);
        recommendation.EntryRequestedAtUtc.Should().BeNull();
        recommendation.EntryExecutionNote.Should().Be("broker-rejected");
        Some(operations.LatestEntryCommand(entry.SourceSignalId)).Status
            .Should().Be(TradingCommandStatuses.Rejected);
    }

    [Fact]
    public void BrokerEvidenceConvergesAcrossPartialFillRestartMismatchAndExpiry()
    {
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, "convergence.db");
        var config = new ServiceConfig(
            database, new string('x', 32), "unused", "unused", "unused",
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            TradingAuthorityMode.Projection);
        var now = DateTime.UtcNow;
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        var snapshot = EmptySnapshot(now);
        operations.Import(snapshot);
        operations.ApplyAccountConfiguration(AccountConfiguration(now));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Shadow, 2, "shadow", now,
            snapshot.SnapshotId, string.Empty, null, 0));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Remote, 3, "remote", now,
            snapshot.SnapshotId, "broker-state", now, 0));

        var entry = EntryIntent(now, operations.Status());
        operations.AcceptEntry(entry);
        operations.ClaimEntry().Should().NotBeNull();
        operations.RecordBrokerEvidence(entry.Envelope.CommandId, new BrokerOrderEvidence(
            "entry-converge", "client-entry-converge", "AAPL", "Buy", 10, 4, null, 101m,
            "PartiallyFilled", "Market", now, null)).Should().BeTrue();
        Some(operations.CommandStatus(entry.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.AwaitingBrokerEvidence);
        operations.Portfolio().Positions.Should().BeEmpty();

        // A new process instance must continue evidence reconciliation, never submit the order again.
        var restartedStore = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        operations = new TradingCoreOperations(restartedStore);
        operations.ClaimEntry().Should().BeNull();
        operations.RecordBrokerEvidence(entry.Envelope.CommandId, new BrokerOrderEvidence(
            "entry-converge", "client-entry-converge", "AAPL", "Buy", 10, 4, null, 101m,
            "Cancelled", "Market", now, null)).Should().BeTrue();

        var position = operations.Portfolio().Positions.Single();
        position.Quantity.Should().Be(4);
        position.OpenedAtUtc.Should().BeAfter(now);
        operations.Portfolio().Recommendations.Single().EntryExecutionNote
            .Should().Be("broker-cancelled-after-partial-fill");
        var exitAt = DateTime.UtcNow;
        var exit = PositionCommand(exitAt, operations.Status(), position);
        operations.AcceptPosition(exit);
        operations.ClaimPosition().Should().NotBeNull();
        operations.RecordPositionBrokerEvidence(exit.Envelope.CommandId, new BrokerOrderEvidence(
            "exit-converge", "client-exit-converge", "AAPL", "Sell", 4, 2, null, 109m,
            "PartiallyFilled", "Market", exitAt, null)).Should().BeTrue();
        Some(operations.CommandStatus(exit.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.AwaitingBrokerEvidence);
        operations.Portfolio().Positions.Single().Quantity.Should().Be(4);

        // A terminal response with incompatible quantity is evidence to reconcile, not a fill.
        operations.RecordPositionBrokerEvidence(exit.Envelope.CommandId, new BrokerOrderEvidence(
            "exit-converge", "client-exit-converge", "AAPL", "Sell", 4, 3, null, 109m,
            "Filled", "Market", exitAt, exitAt.AddSeconds(2)))
            .Should().BeTrue();
        Some(operations.CommandStatus(exit.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.ReconciliationRequired);
        operations.Portfolio().Positions.Single().Quantity.Should().Be(4);
        operations.Portfolio().Trades.Should().BeEmpty();

        operations.RecordPositionBrokerEvidence(exit.Envelope.CommandId, new BrokerOrderEvidence(
            "exit-converge", "client-exit-converge", "AAPL", "Sell", 4, 2, null, 110m,
            "Cancelled", "Market", exitAt, null))
            .Should().BeTrue();
        Some(operations.CommandStatus(exit.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.Completed);
        operations.Portfolio().Positions.Single().Quantity.Should().Be(2);
        operations.Portfolio().Positions.Single().ClosedAtUtc.Should().BeNull();
        operations.Portfolio().Trades.Should().ContainSingle().Which.Quantity.Should().Be(2);

        var expiring = EntryIntent(DateTime.UtcNow, operations.Status());
        expiring = expiring with
        {
            Envelope = expiring.Envelope with { CommandId = "entry-expiring" },
            SourceSignalId = "signal-expiring"
        };
        expiring = expiring with
        {
            Envelope = expiring.Envelope with
            {
                PayloadHash = TradingCoreIdentity.EntryPayload(expiring)
            }
        };
        operations.AcceptEntry(expiring);
        operations.RejectExpiredPendingIntents(expiring.Envelope.ExpiresAtUtc.AddTicks(1))
            .Should().Be(1);
        operations.ClaimEntry().Should().BeNull();
        Some(operations.CommandStatus(expiring.Envelope.CommandId)).Status
            .Should().Be(TradingCommandStatuses.Rejected);
        operations.Portfolio().Recommendations.Single(value =>
            value.SourceSignalId == expiring.SourceSignalId).EntryRequestedAtUtc.Should().BeNull();
    }

    private static TradingStateSnapshot EmptySnapshot(DateTime now)
    {
        var value = new TradingStateSnapshot(1, string.Empty, 1, now,
            [], [], [], [], new TradingRiskProjection(-12m, -0.12m, 0, false, now));
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

    private static TradingRecommendationObservation RecommendationObservation(
        DateTime now, TradingCoreStatus status)
    {
        var intent = EntryIntent(now, status);
        var envelope = new TradingCommandEnvelope(
            1, "recommendation-command", TradingCommandKinds.RecordRecommendation,
            string.Empty, "correlation", null, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes(5));
        var observation = new TradingRecommendationObservation(
            envelope, "signal-alert", intent.Symbol, intent.PatternCode,
            intent.CustomPatternName, intent.EntryPrice, intent.StopLossPrice,
            intent.TargetPrice, intent.ShareQuantity, intent.Expectancy,
            intent.ExecutionArtifact, intent.MarketDataEvidence);
        return observation with
        {
            Envelope = envelope with
            {
                PayloadHash = TradingCoreIdentity.RecommendationPayload(observation)
            }
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

    private static TradingPositionPolicyStateUpdate PositionStateUpdate(
        DateTime now, TradingCoreStatus status, TradingPositionProjection position)
    {
        var envelope = new TradingCommandEnvelope(
            1, "position-state-command", TradingCommandKinds.UpdatePositionState,
            string.Empty, "correlation", null, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes(5));
        var update = new TradingPositionPolicyStateUpdate(
            envelope, position.PositionId, position.ExecutionContext!.ExecutionArtifact.ArtifactId,
            105m, 96m, position.InitialRiskDistance, true, false, Evidence(now));
        return update with
        {
            Envelope = envelope with
            {
                PayloadHash = TradingCoreIdentity.PositionStatePayload(update)
            }
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

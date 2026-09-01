using System.Text.Json;
using FluentAssertions;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.Broker;
using StockTrader.TradingCore.Execution;
using StockTrader.TradingCoreService;
using Microsoft.FSharp.Core;

namespace StockTrader.Tests;

public sealed class TradingCoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"trading-core-{Guid.NewGuid():N}");

    [Fact]
    public void PreCommitAbortConsumesGenerationAndRequiresExplicitRelease()
    {
        var path = Path.Combine(_root, "transition.db");
        var config = CreateConfig(path, TradingAuthorityMode.Shadow);
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE state SET value='1' WHERE key='account_generation'";
            command.ExecuteNonQuery();
        }

        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var transitionId = Guid.NewGuid().ToString();
        var createOperation = new TradingControlOperation(
            TradingControlContractVersions.Current, Guid.NewGuid().ToString(), string.Empty,
            "transition-test", null, now);
        var create = new AuthorityTransitionRequest(
            createOperation, transitionId, AuthorityTransitionDirections.Cutover,
            TradingAuthorityMode.Shadow, TradingAuthorityMode.Remote, 1, 1,
            now, now.AddMinutes(30));
        createOperation = createOperation with
        {
            PayloadHash = TradingControlIdentity.Transition(create)
        };
        create = create with { Operation = createOperation };

        operations.CreateTransition(create).Phase.Should().Be(AuthorityTransitionPhases.Requested);
        operations.CreateTransition(create).AlreadyApplied.Should().BeTrue();
        operations.AuthorityV2().CommandAcceptance.Should().Be(AuthorityCommandAcceptanceStates.Fenced);

        var abort = Step(transitionId, AuthorityTransitionOperations.Abort,
            AuthorityTransitionPhases.Requested, now.AddMinutes(1));
        var aborted = operations.ApplyTransitionStep(abort);
        aborted.Phase.Should().Be(AuthorityTransitionPhases.ReadyToRelease);
        aborted.Outcome.Should().Be(AuthorityTransitionOutcomes.SourceRetained);
        operations.Authority().Generation.Should().Be(2);
        operations.Authority().Mode.Should().Be(TradingAuthorityMode.Shadow);
        operations.AuthorityV2().CommandAcceptance.Should().Be(AuthorityCommandAcceptanceStates.Fenced);

        var release = Step(transitionId, AuthorityTransitionOperations.Release,
            AuthorityTransitionPhases.ReadyToRelease, now.AddMinutes(2));
        operations.ApplyTransitionStep(release).Phase.Should().Be(AuthorityTransitionPhases.Completed);
        operations.AuthorityV2().CommandAcceptance.Should().Be(AuthorityCommandAcceptanceStates.Open);
    }

    [Fact]
    public void SealedFinancialTransferMustBeImportedBeforeReconciliation()
    {
        var path = Path.Combine(_root, "financial-transfer.db");
        var config = CreateConfig(path, TradingAuthorityMode.Shadow);
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        var now = new DateTime(2026, 9, 1, 13, 0, 0, DateTimeKind.Utc);
        var accountConfiguration = AccountConfiguration(now);
        operations.ApplyAccountConfiguration(accountConfiguration);

        var transitionId = Guid.NewGuid().ToString();
        var createOperation = new TradingControlOperation(
            TradingControlContractVersions.Current, Guid.NewGuid().ToString(), string.Empty,
            "transfer-test", null, now);
        var create = new AuthorityTransitionRequest(
            createOperation, transitionId, AuthorityTransitionDirections.Cutover,
            TradingAuthorityMode.Shadow, TradingAuthorityMode.Remote, 1, 1,
            now, now.AddMinutes(30));
        createOperation = createOperation with
        {
            PayloadHash = TradingControlIdentity.Transition(create)
        };
        operations.CreateTransition(create with { Operation = createOperation });

        var sourceFence = Fence(AuthorityOwners.Edge, now);
        var targetFence = Fence(AuthorityOwners.TradingCore, now);
        operations.ApplyTransitionStep(Step(
            transitionId, AuthorityTransitionOperations.Quiesce,
            AuthorityTransitionPhases.Requested, now.AddSeconds(1),
            sourceFence, targetFence));
        var drain = new AuthorityDrainInventory(0, 0, 0, 0, 0, now.AddSeconds(2), string.Empty);
        drain = drain with { InventoryHash = TradingControlIdentity.Drain(drain) };
        operations.ApplyTransitionStep(Step(
            transitionId, AuthorityTransitionOperations.Drain,
            AuthorityTransitionPhases.Quiescing, now.AddSeconds(2),
            drainInventory: drain));

        var snapshot = EmptySnapshot(now.AddSeconds(3));
        var activity = CanonicalFinancialTransferMapper.Activity(
            new Dictionary<string, long>(), 0, Array.Empty<FinancialConsumerCursor>());
        var transfer = CanonicalFinancialTransferMapper.Create(
            Guid.NewGuid().ToString(), transitionId, AuthorityTransitionDirections.Cutover,
            TradingAuthorityMode.Shadow, 2,
            new FinancialTransferCompatibility(2, "edge-v1", "trading-core-v1",
                "engine-v1", "artifact-v1", "patterns-v1", "calendar-v1", "market-data-v1"),
            accountConfiguration, snapshot,
            Array.Empty<FinancialExecutionIdentity>(), Array.Empty<FinancialBrokerEvidence>(),
            activity, "broker-equity");

        operations.ImportFinancialTransfer(transfer).AlreadyApplied.Should().BeFalse();
        operations.ImportFinancialTransfer(transfer).AlreadyApplied.Should().BeTrue();
        var reconciliation = new AuthorityReconciliationEvidence(
            snapshot.SnapshotId, "broker-reconciled", now.AddSeconds(4), 0,
            transfer.TransferId, transfer.TransferHash);
        operations.ApplyTransitionStep(Step(
                transitionId, AuthorityTransitionOperations.Reconcile,
                AuthorityTransitionPhases.Draining, now.AddSeconds(4),
                drainInventory: drain, reconciliation: reconciliation))
            .Phase.Should().Be(AuthorityTransitionPhases.Reconciled);
    }

    [Fact]
    public void CorruptDatabaseFailsClosedInsteadOfBeingReinitialized()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "corrupt.db");
        var config = CreateConfig(path);
        _ = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        using (var file = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None))
            file.SetLength(128);

        var construct = () => new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("trading-core-database-integrity-check-failed");
    }

    [Fact]
    public void ProjectionPortfolioReadsImportedFinancialRowsInsteadOfEmptyCanonicalTables()
    {
        Directory.CreateDirectory(_root);
        var config = CreateConfig(Path.Combine(_root, "projection.db"));
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
        var config = CreateConfig(Path.Combine(_root, "core.db"));
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
        var config = CreateConfig(Path.Combine(_root, "core.db"));
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
        var config = CreateConfig(database);
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

    [Fact]
    public void ShadowEntryComparisonIsIdempotentAndNeverCreatesFinancialState()
    {
        Directory.CreateDirectory(_root);
        var config = CreateConfig(Path.Combine(_root, "shadow.db"));
        var store = new TradingCoreStore(
            config, new JsonSerializerOptions(JsonSerializerDefaults.Web), new SecretStore(config));
        var operations = new TradingCoreOperations(store);
        var openAt = new DateTime(2026, 8, 26, 15, 0, 0, DateTimeKind.Utc);
        var snapshot = EmptySnapshot(openAt);
        operations.Import(snapshot);
        operations.ApplyAccountConfiguration(AccountConfiguration(openAt));
        operations.Activate(new TradingAuthorityContract(
            1, TradingAuthorityMode.Shadow, 2, "shadow-entry-comparison", openAt,
            snapshot.SnapshotId, string.Empty, null, 0));

        var open = ShadowObservation(openAt, operations.Status(),
            TradingShadowDispositions.BrokerSubmission, null);
        var receipt = operations.CompareShadowEntry(open);
        receipt.IsMatch.Should().BeTrue();
        receipt.CandidateDisposition.Should().Be(TradingShadowDispositions.BrokerSubmission);
        operations.CompareShadowEntry(open).AlreadyApplied.Should().BeTrue();

        var closedAt = openAt.AddHours(-5);
        var closed = ShadowObservation(closedAt, operations.Status(),
            TradingShadowDispositions.Blocked, "market-closed");
        receipt = operations.CompareShadowEntry(closed);
        receipt.IsMatch.Should().BeTrue();
        receipt.CandidateReason.Should().Be("market-closed");

        var mismatch = ShadowObservation(openAt.AddMinutes(1), operations.Status(),
            TradingShadowDispositions.Blocked, "forced-authoritative-mismatch");
        operations.CompareShadowEntry(mismatch).IsMatch.Should().BeFalse();
        var summary = operations.ShadowSummary();
        summary.Total.Should().Be(3);
        summary.Matches.Should().Be(2);
        summary.Mismatches.Should().Be(1);
        operations.CommandStatus(open.Intent.Envelope.CommandId).Should().BeNull();
        operations.Portfolio().Recommendations.Should().BeEmpty();
        operations.Portfolio().Positions.Should().BeEmpty();
        operations.Portfolio().Trades.Should().BeEmpty();

        var projectedPosition = new TradingPositionProjection(
            "local-position-1", open.Intent.SourceSignalId, open.Intent.AccountId,
            open.Intent.Symbol, open.Intent.Sector, 4, 4, 101m, 102m,
            open.Intent.StopLossPrice, open.Intent.TargetPrice, open.Intent.PatternCode,
            open.Intent.CustomPatternName, openAt.AddMinutes(2), null, null, 102m,
            1m, 6m, false, false, false, null, null, null, false,
            null, null, null, [], null);
        var projectedValue = new TradingStateSnapshot(
            TradingCoreContractVersions.Current, string.Empty, 2, openAt.AddMinutes(2),
            [], [], [projectedPosition], [],
            new TradingRiskProjection(0m, 0m, 1, false, openAt.AddMinutes(2)));
        var projected = projectedValue with
        {
            SnapshotId = TradingCoreIdentity.Snapshot(projectedValue)
        };
        operations.Import(projected).Should().BeFalse();
        var enriched = operations.Portfolio().Positions.Single();
        enriched.ExecutionContext.Should().NotBeNull();
        enriched.ExecutionContext!.ExecutionArtifact.ArtifactId
            .Should().Be(open.Intent.ExecutionArtifact.ArtifactId);
        enriched.ExecutionContext.EntryMarketDataEvidence.EvidenceId
            .Should().Be(open.Intent.MarketDataEvidence.EvidenceId);

        var noAction = ShadowPositionObservation(
            enriched, open.Intent.MarketDataEvidence, openAt.AddMinutes(3),
            TradingShadowDispositions.NoAction, null, null,
            TradingShadowDispositions.NoAction, null, null);
        operations.CompareShadowPosition(noAction).IsMatch.Should().BeTrue();
        operations.CompareShadowPosition(noAction).AlreadyApplied.Should().BeTrue();
        var positionMismatch = ShadowPositionObservation(
            enriched, open.Intent.MarketDataEvidence, openAt.AddMinutes(4),
            TradingShadowDispositions.PositionCommand,
            TradingPositionActionKinds.FullExit, 2,
            TradingShadowDispositions.NoAction, null, null);
        operations.CompareShadowPosition(positionMismatch).IsMatch.Should().BeFalse();
        var policyMismatch = noAction with
        {
            DecisionId = string.Empty,
            PayloadHash = string.Empty,
            ObservedAtUtc = openAt.AddMinutes(5),
            CandidatePolicyState = noAction.CandidatePolicyState with
            {
                TrailingStopActivated = !noAction.CandidatePolicyState.TrailingStopActivated,
            },
        };
        var policyHash = TradingCoreIdentity.ShadowPositionPayload(policyMismatch);
        policyMismatch = policyMismatch with
        {
            DecisionId = $"shadow-position:{policyHash}",
            PayloadHash = policyHash,
        };
        var policyReceipt = operations.CompareShadowPosition(policyMismatch);
        policyReceipt.IsPolicyStateMatch.Should().BeFalse();
        policyReceipt.IsMatch.Should().BeFalse();
        summary = operations.ShadowSummary();
        summary.Total.Should().Be(6);
        summary.Matches.Should().Be(3);
        summary.Mismatches.Should().Be(3);
        operations.CommandStatus(open.Intent.Envelope.CommandId).Should().BeNull();
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
        var management = new TradingPositionManagementArtifact(
            new TradingLongPositionPolicy(
                20, true, 2.5m, 1m, true, 2m, true, true,
                1.5m, "stop", "protected-stop"),
            50, null, null);
        var artifactHash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings,
            evidence.CalendarVersion, management);
        var artifact = new TradingStrategyExecutionArtifact(1, artifactHash,
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings,
            artifactHash, OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion, evidence.CalendarVersion,
            true, true, management);
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

    private static TradingShadowEntryObservation ShadowObservation(
        DateTime observedAt,
        TradingCoreStatus status,
        string disposition,
        string? reason)
    {
        var intent = EntryIntent(observedAt, status);
        var observation = new TradingShadowEntryObservation(
            TradingCoreContractVersions.Current, string.Empty, string.Empty, observedAt,
            "AutoOrder", disposition, reason, intent);
        var payloadHash = TradingCoreIdentity.ShadowEntryPayload(observation);
        return observation with
        {
            DecisionId = $"shadow:{payloadHash}",
            PayloadHash = payloadHash
        };
    }

    private static TradingShadowPositionObservation ShadowPositionObservation(
        TradingPositionProjection position,
        MarketDataEvidenceContract evidence,
        DateTime observedAt,
        string authoritativeDisposition,
        string? authoritativeAction,
        int? authoritativeQuantity,
        string candidateDisposition,
        string? candidateAction,
        int? candidateQuantity)
    {
        var observation = new TradingShadowPositionObservation(
            TradingCoreContractVersions.Current, string.Empty, string.Empty, observedAt,
            position.PositionId, CanonicalJsonHash.Compute(position),
            position.ExecutionContext!.ExecutionArtifact.ArtifactId, evidence,
            authoritativeDisposition, authoritativeAction, authoritativeQuantity, null,
            PositionPolicyState(position),
            candidateDisposition, candidateAction, candidateQuantity, null,
            PositionPolicyState(position));
        var hash = TradingCoreIdentity.ShadowPositionPayload(observation);
        return observation with
        {
            DecisionId = $"shadow-position:{hash}",
            PayloadHash = hash
        };
    }

    private static TradingShadowPositionPolicyState PositionPolicyState(
        TradingPositionProjection position) => new(
        position.HighSinceEntry,
        position.StopLossPrice,
        position.InitialRiskDistance,
        position.BreakevenApplied,
        position.TrailingStopActivated);

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
            105m, 96m, position.InitialRiskDistance, true, false, Evidence(now),
            position.EntryAtr, now, 1);
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

    private static AuthorityTransitionStepRequest Step(
        string transitionId,
        string step,
        string expectedPhase,
        DateTime observedAt,
        AuthorityFenceReceipt? sourceFence = null,
        AuthorityFenceReceipt? targetFence = null,
        AuthorityDrainInventory? drainInventory = null,
        AuthorityReconciliationEvidence? reconciliation = null)
    {
        var operation = new TradingControlOperation(
            TradingControlContractVersions.Current, Guid.NewGuid().ToString(), string.Empty,
            "transition-test", null, observedAt);
        var request = new AuthorityTransitionStepRequest(
            operation, transitionId, step, expectedPhase, sourceFence, targetFence,
            drainInventory, reconciliation,
            null, null, Array.Empty<string>());
        operation = operation with { PayloadHash = TradingControlIdentity.Step(request) };
        return request with { Operation = operation };
    }

    private static AuthorityFenceReceipt Fence(string owner, DateTime observedAt)
    {
        var receipt = new AuthorityFenceReceipt(
            owner, 1, AuthorityCommandAcceptanceStates.Fenced,
            AuthorityCommandAcceptanceStates.Fenced, "AtBarrier", "Clear", "Clear",
            observedAt, 0, 0, 0, 0, string.Empty);
        return receipt with { FenceHash = TradingControlIdentity.Fence(receipt) };
    }

    private static ServiceConfig CreateConfig(
        string databasePath,
        TradingAuthorityMode initialMode = TradingAuthorityMode.Projection) => new(
        databasePath,
        "unused-server-cert",
        "unused-server-key",
        "unused-client-ca",
        "edge-trading-control.stocktrader.internal",
        "trading-cutover-coordinator.stocktrader.internal",
        new Uri("https://market-data.test"),
        "unused-market-data-client-cert",
        "unused-market-data-client-key",
        "unused-market-data-server-ca",
        "market-data.stocktrader.internal",
        TimeSpan.FromSeconds(30),
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
        "test-generation",
        true,
        initialMode);

    private static T Some<T>(FSharpOption<T>? value)
    {
        value.Should().NotBeNull();
        return value!.Value;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

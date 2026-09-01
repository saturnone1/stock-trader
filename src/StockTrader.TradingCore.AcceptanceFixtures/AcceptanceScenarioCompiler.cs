using System.Text.Json;
using StockTrader.Domain.MarketData;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.AcceptanceFixtures;

public static class AcceptanceScenarioCompiler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string AccountId = "acceptance-paper";

    public static AcceptanceScenarioFixture Compile(
        AcceptanceScenarioDefinition definition,
        MarketDataExecutionWindowResponse marketData)
    {
        var definitionError = TradingCoreAcceptancePolicy.DefinitionError(definition);
        if (definitionError is not null) throw new ArgumentException(definitionError, nameof(definition));
        if (marketData is null || !marketData.Evidence.IsComplete
            || marketData.Bars.Count != definition.RequiredBars
            || !string.Equals(marketData.Evidence.Symbol, definition.Symbol, StringComparison.Ordinal)
            || !string.Equals(marketData.Evidence.Provider, definition.Provider, StringComparison.Ordinal)
            || !string.Equals(marketData.Evidence.CalendarVersion, definition.CalendarVersion, StringComparison.Ordinal))
            throw new ArgumentException("acceptance-market-data-evidence-incomplete", nameof(marketData));

        var now = NextOpenObservation(marketData.Evidence.LastBarUtc!.Value);
        var artifact = Artifact(marketData.Evidence);
        var commandId = $"acceptance-{definition.ScenarioCode}";
        var signalId = $"acceptance-signal-{definition.ScenarioCode}";
        var intent = EntryIntent(commandId, signalId, definition.Symbol, now, artifact, marketData.Evidence);
        var initialPosition = UsesOpenPosition(definition.ScenarioCode)
            ? Position(definition, artifact, marketData.Evidence, now)
            : null;
        var snapshot = Snapshot(definition, initialPosition, now);
        var accountConfiguration = AccountConfiguration(now);
        var authority = new TradingAuthorityContract(
            TradingCoreContractVersions.Current, TradingAuthorityMode.Remote, 2,
            $"acceptance-{definition.ScenarioId}", now, snapshot.SnapshotId,
            CanonicalJsonHash.Compute(new { account = AccountId, reconciledAtUtc = now }),
            now, 0);
        var brokerPlan = BrokerPlan(definition, intent, initialPosition, now);
        var operations = Operations(definition, intent, brokerPlan, now);
        var assertions = Assertions(definition, initialPosition);
        var expectedStateHash = TradingCoreAcceptanceIdentity.ExpectedState(assertions);
        var bootstrap = new AcceptanceBootstrapRequest(
            definition.ScenarioId, $"bootstrap-{definition.ScenarioId}", "pending",
            snapshot, accountConfiguration, authority);
        var pending = new AcceptanceScenarioFixture(
            TradingCoreAcceptanceVersions.Current, "", brokerPlan, bootstrap,
            operations, assertions, expectedStateHash, null);
        var fixtureHash = TradingCoreAcceptanceIdentity.Fixture(pending);
        var fixture = pending with
        {
            FixtureHash = fixtureHash,
            Bootstrap = bootstrap with { FixtureHash = fixtureHash }
        };
        // Bootstrap participates in the fixture hash. Recompute once after inserting it, then seal
        // both copies with the final value.
        fixture = fixture with { FixtureHash = "", Bootstrap = fixture.Bootstrap with { FixtureHash = "" } };
        fixtureHash = TradingCoreAcceptanceIdentity.Fixture(fixture);
        fixture = fixture with
        {
            FixtureHash = fixtureHash,
            Bootstrap = fixture.Bootstrap with { FixtureHash = fixtureHash }
        };
        return fixture;
    }

    public static MarketDataExecutionWindowRequest EvidenceRequest(
        AcceptanceScenarioDefinition definition, DateOnly expectedSession)
    {
        var through = expectedSession.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var lookback = MarketDataExecutionEvidenceLimits.RequiredDailyLookbackCalendarDays(
            definition.RequiredBars);
        return new MarketDataExecutionWindowRequest(
            MarketDataContractVersions.Current, definition.Provider, definition.Symbol, "Daily",
            definition.AdjustmentMode, definition.Market, definition.CalendarVersion,
            through.AddDays(-lookback), through, definition.RequiredBars, expectedSession);
    }

    private static DateTime NextOpenObservation(DateTime lastBarUtc)
    {
        var candidate = lastBarUtc.Date.AddDays(1);
        while (!ExchangeCalendarCatalog.GetTradingDay(
                   MarketRegion.UnitedStates, DateOnly.FromDateTime(candidate)).IsTradingDay)
            candidate = candidate.AddDays(1);
        // 15:00 UTC is inside regular US hours in both standard and daylight time.
        return DateTime.SpecifyKind(candidate.AddHours(15), DateTimeKind.Utc);
    }

    private static TradingStrategyExecutionArtifact Artifact(MarketDataEvidenceContract evidence)
    {
        const string settings = "{\"patternConfiguration\":{},\"exitPolicy\":{}}";
        var management = new TradingPositionManagementArtifact(
            new TradingLongPositionPolicy(20, true, 2.5m, 1m, true, 2m, true, true,
                1.5m, "stop", "protected-stop"),
            50, null, null);
        var hash = TradingExecutionArtifactPolicy.ComputeDefinitionHash(
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings,
            evidence.CalendarVersion, management);
        return new TradingStrategyExecutionArtifact(
            TradingCoreContractVersions.Current, hash,
            TradingExecutionArtifactKinds.BuiltInPattern, "Breakout", null, settings, hash,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
            evidence.CalendarVersion, true, true, management);
    }

    private static TradingEntryIntent EntryIntent(string commandId, string signalId, string symbol,
        DateTime now, TradingStrategyExecutionArtifact artifact, MarketDataEvidenceContract evidence)
    {
        var envelope = new TradingCommandEnvelope(
            TradingCoreContractVersions.Current, commandId, TradingCommandKinds.AcceptEntry, "",
            $"correlation-{commandId}", null, 2, 1, now, now.AddHours(1));
        var intent = new TradingEntryIntent(
            envelope, signalId, AccountId, symbol, "Technology", "Breakout", null,
            100m, 95m, 120m, 10, 0.1m, artifact, evidence);
        return intent with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.EntryPayload(intent) }
        };
    }

    private static TradingPositionProjection Position(AcceptanceScenarioDefinition definition,
        TradingStrategyExecutionArtifact artifact, MarketDataEvidenceContract evidence, DateTime now) =>
        new($"acceptance-position-{definition.ScenarioCode}", null, AccountId,
            definition.Symbol, "Technology", 10, 10, 100m, 101m, 95m, 120m,
            "Breakout", null, now.AddDays(-1), null, null, 103m, 2m, 5m,
            false, false, false, null, null, null, false, null, null, null, [],
            new TradingPositionExecutionContext(artifact, evidence), evidence.EvidenceId,
            evidence.LastBarUtc, evidence.Revision);

    private static TradingStateSnapshot Snapshot(AcceptanceScenarioDefinition definition,
        TradingPositionProjection? position, DateTime now)
    {
        var candidate = new TradingStateSnapshot(
            TradingCoreContractVersions.Current, "", 1, now,
            [new TradingAccountProjection(AccountId, "Alpaca", "Paper", true, true, 1)],
            [], position is null ? [] : [position], [],
            new TradingRiskProjection(0m, 0m, position is null ? 0 : 1, false, now));
        return candidate with
        {
            SnapshotId = TradingCoreIdentity.Snapshot(candidate)
        };
    }

    private static TradingAccountConfigurationSet AccountConfiguration(DateTime now)
    {
        var candidate = new TradingAccountConfigurationSet(
            TradingCoreContractVersions.Current, 1, "", now,
            [new TradingAccountConfiguration(AccountId, "Alpaca", "Paper", true, true,
                "acceptance-key", "acceptance-secret")],
            new TradingRiskConfiguration(0.01m, 0.03m, 20, 10));
        return candidate with
        {
            ConfigurationHash = TradingCoreIdentity.AccountConfiguration(candidate)
        };
    }

    private static ScriptedBrokerPlan BrokerPlan(AcceptanceScenarioDefinition definition,
        TradingEntryIntent intent, TradingPositionProjection? position, DateTime now)
    {
        var initialPositions = position is null
            ? Array.Empty<ScriptedBrokerPosition>()
            : [new ScriptedBrokerPosition(position.Symbol, position.Quantity, "100", "101")];
        var account = new ScriptedBrokerAccount(AccountId, "100000", "100000", "100000",
            "100000", false, now);
        var steps = new List<ScriptedBrokerStep>();
        // Restarts may repeat read-only portfolio probes. Every financial mutation remains exact,
        // while a bounded read allowance keeps the Pod-loss scenarios scheduler-independent.
        for (var ordinal = 0; ordinal < 8; ordinal++)
        {
            steps.Add(Step(ScriptedBrokerOperations.GetAccount, ordinal));
            steps.Add(Step(ScriptedBrokerOperations.GetPositions, ordinal));
        }

        if (UsesEntryCommand(definition.ScenarioCode))
        {
            var clientId = FinancialExecutionIdentityPolicy.ClientOrderId(intent.Envelope.CommandId);
            var terminal = Order(intent, clientId, "Filled", 10, now);
            switch (definition.ScenarioCode)
            {
                case "broker-rejection-before-fill":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenReturn,
                        Order(intent, clientId, "Rejected", 0, now), clientId));
                    break;
                case "broker-timeout-before-submission-proof":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.ThrowWithoutEffect, null, clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0));
                    break;
                case "broker-accepted-then-timeout":
                case "trading-core-pod-loss":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenTimeout, terminal, clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0,
                        ScriptedBrokerActions.ReturnEvidence, terminal));
                    break;
                case "delayed-out-of-order-partial-fills":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenReturn,
                        Order(intent, clientId, "PartiallyFilled", 4, now), clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0,
                        ScriptedBrokerActions.ReturnOutOfOrderEvidence,
                        Order(intent, clientId, "PartiallyFilled", 2, now), barrier: null));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 1,
                        ScriptedBrokerActions.ReturnEvidence, terminal));
                    break;
                case "cancellation-with-partial-fill":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenReturn,
                        Order(intent, clientId, "Cancelled", 4, now), clientId));
                    break;
                case "contradictory-terminal-quantity":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenReturn,
                        Order(intent, clientId, "Filled", 9, now), clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0,
                        ScriptedBrokerActions.ReturnEvidence,
                        Order(intent, clientId, "Filled", 9, now)));
                    break;
                case "duplicate-broker-response":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenTimeout, terminal, clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0,
                        ScriptedBrokerActions.ReturnDuplicateEvidence, terminal));
                    break;
                case "broker-outage-and-recovery":
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenTimeout, terminal, clientId));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 0,
                        ScriptedBrokerActions.EnterOutageUntilBarrier, null, barrier: "outage-cleared"));
                    steps.Add(Step(ScriptedBrokerOperations.GetOrders, 1,
                        ScriptedBrokerActions.ReturnEvidence, terminal));
                    break;
                default:
                    steps.Add(Step(ScriptedBrokerOperations.SubmitEntry, 0,
                        ScriptedBrokerActions.RecordThenReturn, terminal, clientId));
                    break;
            }
        }
        var candidate = new ScriptedBrokerPlan(
            TradingCoreAcceptanceVersions.Current, definition.ScenarioCode,
            definition.ScenarioId, "", now, account, initialPositions, steps);
        return candidate with { PlanHash = TradingCoreAcceptanceIdentity.Plan(candidate) };
    }

    private static ScriptedBrokerStep Step(string operation, int ordinal,
        string action = ScriptedBrokerActions.ReturnEvidence,
        ScriptedBrokerOrder? evidence = null, string? clientId = null, string? barrier = null) =>
        new(operation, clientId, ordinal, action, evidence, barrier);

    private static ScriptedBrokerOrder Order(TradingEntryIntent intent, string clientId,
        string status, int filled, DateTime now) =>
        new($"order-{intent.Envelope.CommandId}", clientId, intent.Symbol, "Buy",
            intent.ShareQuantity, filled, "100", filled > 0 ? "100" : null, status,
            "Bracket", now, status is "Filled" or "Cancelled" ? now : null);

    private static IReadOnlyList<AcceptanceDriverOperation> Operations(
        AcceptanceScenarioDefinition definition, TradingEntryIntent intent,
        ScriptedBrokerPlan brokerPlan, DateTime now)
    {
        var values = new List<AcceptanceDriverOperation>();
        if (UsesEntryCommand(definition.ScenarioCode))
        {
            values.Add(Http("submit-entry", AcceptanceDriverTargets.TradingCore, "POST",
                "/v1/commands/entries", intent, 202));
            if (definition.ScenarioCode == "duplicate-command-delivery"
                || definition.ScenarioCode == "accepted-resource-load")
            {
                var count = definition.ScenarioCode == "accepted-resource-load" ? 32 : 1;
                for (var index = 0; index < count; index++)
                    values.Add(Http($"duplicate-entry-{index}", AcceptanceDriverTargets.TradingCore,
                        "POST", "/v1/commands/entries", intent, 202));
            }
            if (definition.ScenarioCode == "command-identity-conflict")
            {
                var conflict = intent with { TargetPrice = intent.TargetPrice + 1 };
                conflict = conflict with
                {
                    Envelope = conflict.Envelope with
                    {
                        PayloadHash = TradingCoreIdentity.EntryPayload(conflict)
                    }
                };
                values.Add(Http("conflicting-entry", AcceptanceDriverTargets.TradingCore, "POST",
                    "/v1/commands/entries", conflict, 409));
            }
            if (definition.ScenarioCode == "broker-outage-and-recovery")
                values.Add(Http("clear-outage", AcceptanceDriverTargets.BrokerControl, "POST",
                    "/control/barriers", new ScriptedBrokerBarrierRequest("outage-cleared"), 200));
            if (definition.ScenarioCode == "trading-core-pod-loss")
                values.Add(new AcceptanceDriverOperation("delete-core-pod",
                    AcceptanceDriverTargets.DeleteTradingCorePod, "DELETE", "/", null,
                    200, null, 1));

            var terminal = ExpectedTerminalStatus(definition.ScenarioCode);
            if (terminal is not null)
            {
                var expected = new TradingCommandStatusView(
                    TradingCoreContractVersions.Current, intent.Envelope.CommandId,
                    TradingCommandKinds.AcceptEntry, intent.Envelope.PayloadHash, terminal,
                    terminal is TradingCommandStatuses.Completed or TradingCommandStatuses.Rejected
                        ? $"order-{intent.Envelope.CommandId}" : null,
                    now, now);
                values.Add(new AcceptanceDriverOperation("wait-command-terminal",
                    AcceptanceDriverTargets.TradingCore, "GET",
                    $"/v1/commands/{intent.Envelope.CommandId}", null, 200,
                    CanonicalJsonHash.Compute(expected), 100));
            }
        }
        else
        {
            values.Add(new AcceptanceDriverOperation("read-authority",
                AcceptanceDriverTargets.TradingCore, "GET", "/v1/status", null,
                200, null, 10));
        }
        return values;
    }

    private static AcceptanceDriverOperation Http<T>(string id, string target, string method,
        string path, T body, int status) =>
        new(id, target, method, path, JsonSerializer.Serialize(body, Json), status, null, 1);

    private static IReadOnlyList<AcceptanceStateAssertion> Assertions(
        AcceptanceScenarioDefinition definition, TradingPositionProjection? initialPosition)
    {
        var values = new List<AcceptanceStateAssertion>
        {
            new("authority-generation", AcceptanceDriverTargets.TradingCore,
                "/authorityGeneration", "2"),
            new("broker-account-id", AcceptanceDriverTargets.BrokerControl,
                "/account/accountId", JsonSerializer.Serialize(AccountId, Json)),
        };
        var expectedQuantity = ExpectedEntryQuantity(definition.ScenarioCode);
        if (initialPosition is not null)
            expectedQuantity += initialPosition.Quantity;
        if (expectedQuantity > 0)
        {
            values.Add(new("core-position-quantity", AcceptanceDriverTargets.TradingCore,
                "/positions/0/quantity", expectedQuantity.ToString()));
            values.Add(new("broker-position-quantity", AcceptanceDriverTargets.BrokerControl,
                "/positions/0/quantity", expectedQuantity.ToString()));
        }
        else
        {
            values.Add(new("core-has-no-position", AcceptanceDriverTargets.TradingCore,
                "/positions", "[]"));
            values.Add(new("broker-has-no-position", AcceptanceDriverTargets.BrokerControl,
                "/positions", "[]"));
        }
        return values;
    }

    private static bool UsesEntryCommand(string code) => code is not (
        "edge-loss-autonomous-protection" or "evaluated-range-evidence-correction"
        or "isolated-cutover-and-rollback-generation");

    private static bool UsesOpenPosition(string code) => code is
        "edge-loss-autonomous-protection" or "evaluated-range-evidence-correction";

    private static int ExpectedEntryQuantity(string code) => code switch
    {
        "broker-rejection-before-fill" or "broker-timeout-before-submission-proof"
            or "contradictory-terminal-quantity" => 0,
        "cancellation-with-partial-fill" => 4,
        _ when UsesEntryCommand(code) => 10,
        _ => 0,
    };

    private static string? ExpectedTerminalStatus(string code) => code switch
    {
        "broker-timeout-before-submission-proof" or "contradictory-terminal-quantity" =>
            TradingCommandStatuses.ReconciliationRequired,
        "broker-rejection-before-fill" => TradingCommandStatuses.Rejected,
        _ => TradingCommandStatuses.Completed,
    };
}

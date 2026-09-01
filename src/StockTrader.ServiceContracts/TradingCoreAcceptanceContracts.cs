using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.ServiceContracts.TradingCore;

public static class TradingCoreAcceptanceVersions
{
    public const int Current = 1;
}

public static class TradingCoreAcceptanceScenarioCatalog
{
    public static IReadOnlyList<string> Required { get; } =
    [
        "completed-bar-downtime-replay",
        "duplicate-command-delivery",
        "command-identity-conflict",
        "broker-rejection-before-fill",
        "broker-timeout-before-submission-proof",
        "broker-accepted-then-timeout",
        "delayed-out-of-order-partial-fills",
        "cancellation-with-partial-fill",
        "contradictory-terminal-quantity",
        "duplicate-broker-response",
        "broker-outage-and-recovery",
        "trading-core-pod-loss",
        "edge-loss-autonomous-protection",
        "evaluated-range-evidence-correction",
        "accepted-resource-load",
        "isolated-cutover-and-rollback-generation",
    ];

    public static bool IsRequired(string code) => Required.Contains(code, StringComparer.Ordinal);
}

public static class ScriptedBrokerOperations
{
    public const string SubmitEntry = "SubmitEntry";
    public const string IncreasePosition = "IncreasePosition";
    public const string ClosePosition = "ClosePosition";
    public const string CancelOrder = "CancelOrder";
    public const string GetOrders = "GetOrders";
    public const string GetPositions = "GetPositions";
    public const string GetAccount = "GetAccount";
}

public static class ScriptedBrokerActions
{
    public const string ReturnEvidence = "ReturnEvidence";
    public const string RecordThenReturn = "RecordThenReturn";
    public const string ThrowWithoutEffect = "ThrowWithoutEffect";
    public const string RecordThenTimeout = "RecordThenTimeout";
    public const string DelayVisibilityUntilBarrier = "DelayVisibilityUntilBarrier";
    public const string ReturnDuplicateEvidence = "ReturnDuplicateEvidence";
    public const string ReturnOutOfOrderEvidence = "ReturnOutOfOrderEvidence";
    public const string EnterOutageUntilBarrier = "EnterOutageUntilBarrier";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [ReturnEvidence, RecordThenReturn, ThrowWithoutEffect, RecordThenTimeout,
         DelayVisibilityUntilBarrier, ReturnDuplicateEvidence,
         ReturnOutOfOrderEvidence, EnterOutageUntilBarrier], StringComparer.Ordinal);
}

public sealed record ScriptedBrokerAccount(
    string AccountId, string TotalEquity, string PreviousDayEquity, string Cash,
    string BuyingPower, bool IsTradingBlocked, DateTime ObservedAtUtc);

public sealed record ScriptedBrokerPosition(
    string Symbol, int Quantity, string AverageEntryPrice, string CurrentPrice);

public sealed record ScriptedBrokerOrder(
    string OrderId, string ClientOrderId, string Symbol, string Side, int Quantity,
    int FilledQuantity, string? OrderPrice, string? AverageFillPrice, string Status,
    string OrderType, DateTime SubmittedAtUtc, DateTime? FilledAtUtc);

public sealed record ScriptedBrokerStep(
    string Operation,
    string? ClientOrderId,
    int CallOrdinal,
    string Action,
    ScriptedBrokerOrder? Evidence,
    string? Barrier);

public sealed record ScriptedBrokerPlan(
    int ContractVersion,
    string ScenarioCode,
    string ScenarioId,
    string PlanHash,
    DateTime VirtualStartUtc,
    ScriptedBrokerAccount InitialAccount,
    IReadOnlyList<ScriptedBrokerPosition> InitialPositions,
    IReadOnlyList<ScriptedBrokerStep> Steps);

public sealed record ScriptedBrokerCall(
    string Operation,
    string? ClientOrderId,
    string RequestHash,
    DateTime ObservedAtUtc);

public sealed record ScriptedBrokerTerminalState(
    ScriptedBrokerAccount Account,
    IReadOnlyList<ScriptedBrokerPosition> Positions,
    IReadOnlyList<ScriptedBrokerOrder> Orders,
    IReadOnlyList<ScriptedBrokerCall> Journal,
    string StateHash);

public sealed record ScriptedBrokerBarrierRequest(string Name);
public sealed record AcceptanceTimeAdvanceRequest(
    string ScenarioId, string OperationId, DateTime UtcNow, string CausationId);
public sealed record AcceptanceTimeView(string ScenarioId, DateTime UtcNow, long Revision);

public sealed record AcceptanceScenarioDefinition(
    int ContractVersion,
    string ScenarioCode,
    string ScenarioId,
    string Provider,
    string Symbol,
    string AdjustmentMode,
    string Market,
    string CalendarVersion,
    int RequiredBars);

public sealed record AcceptanceBootstrapRequest(
    string ScenarioId,
    string ScenarioCode,
    string OperationId,
    string FixtureHash,
    TradingStateSnapshot Snapshot,
    TradingAccountConfigurationSet AccountConfiguration,
    TradingAuthorityContract RunningAuthority);

public sealed record AcceptanceScenarioStartRequest(string ScenarioId, string OperationId);

public sealed record AcceptanceScenarioState(
    string ScenarioId,
    string ScenarioCode,
    string Phase,
    string FixtureHash,
    IReadOnlyList<string> OperationIds,
    DateTime UpdatedAtUtc);

public static class AcceptanceDriverTargets
{
    public const string TradingCore = "TradingCore";
    public const string TradingCoreStatus = "TradingCoreStatus";
    public const string AcceptanceControl = "AcceptanceControl";
    public const string BrokerControl = "BrokerControl";
    public const string DeleteTradingCorePod = "DeleteTradingCorePod";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        TradingCore, TradingCoreStatus, AcceptanceControl, BrokerControl, DeleteTradingCorePod
    };
}

public sealed record AcceptanceDriverOperation(
    string OperationId,
    string Target,
    string Method,
    string Path,
    string? BodyJson,
    int ExpectedStatus,
    string? ExpectedResponseHash,
    int MaxAttempts);

public sealed record AcceptanceStateAssertion(
    string Name,
    string Target,
    string JsonPointer,
    string ExpectedJson);

public sealed record AcceptanceAssertionObservation(
    string Name,
    string Target,
    string JsonPointer,
    string ValueHash);

public sealed record AcceptanceScenarioFixture(
    int ContractVersion,
    string FixtureHash,
    ScriptedBrokerPlan BrokerPlan,
    AcceptanceBootstrapRequest Bootstrap,
    IReadOnlyList<AcceptanceDriverOperation> Operations,
    IReadOnlyList<AcceptanceStateAssertion> Assertions,
    string ExpectedStateHash,
    string? ExpectedStopReason);

public sealed record AcceptanceScenarioResult(
    string ScenarioId, string ScenarioCode, string FixtureHash, string ExpectedStateHash,
    string ActualStateHash, IReadOnlyList<string> EvidenceReferences,
    DateTime StartedAtUtc, DateTime EndedAtUtc, bool Passed, string? StopReason);

public sealed record AcceptanceManifestV1(
    int ContractVersion, string ManifestId, string RunId, string EnvironmentClass,
    string RepositoryCommit, string BuildId, IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> SharedAssemblyHashes,
    IReadOnlyList<AcceptanceScenarioResult> Scenarios, DateTime StartedAtUtc,
    DateTime EndedAtUtc, bool Passed, IReadOnlyList<string> StopReasons);

public static class TradingCoreAcceptanceImageCatalog
{
    public static IReadOnlyList<string> Required { get; } =
    [
        "edge-local", "edge", "trading-core", "trading-core-shadow", "market-data",
        "acceptance-core", "broker-emulator", "driver", "coordinator", "rollback-importer"
    ];
}

public static class TradingCoreAcceptanceAssemblyCatalog
{
    public static IReadOnlyList<string> Required { get; } =
    ["service-contracts", "engine", "trading-core", "runtime"];
}

public static class TradingCoreAcceptanceIdentity
{
    public static string Plan(ScriptedBrokerPlan plan) =>
        CanonicalJsonHash.Compute(plan, nameof(ScriptedBrokerPlan.PlanHash));
    public static string Manifest(AcceptanceManifestV1 manifest) =>
        CanonicalJsonHash.Compute(manifest, nameof(AcceptanceManifestV1.ManifestId));
    public static string BrokerState(ScriptedBrokerTerminalState state) =>
        CanonicalJsonHash.Compute(state, nameof(ScriptedBrokerTerminalState.StateHash));
    public static string Fixture(AcceptanceScenarioFixture fixture) =>
        CanonicalJsonHash.Compute(fixture, nameof(AcceptanceScenarioFixture.FixtureHash));
    public static string ExpectedState(IReadOnlyList<AcceptanceStateAssertion> assertions) =>
        CanonicalJsonHash.Compute(assertions
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .Select(value => new AcceptanceAssertionObservation(
                value.Name, value.Target, value.JsonPointer,
                CanonicalFinancialTransferIdentity.Payload(value.ExpectedJson)))
            .ToArray());
}

public static class TradingCoreAcceptancePolicy
{
    public static string? DefinitionError(AcceptanceScenarioDefinition definition)
    {
        if (definition is null
            || definition.ContractVersion != TradingCoreAcceptanceVersions.Current
            || !TradingCoreAcceptanceScenarioCatalog.IsRequired(definition.ScenarioCode)
            || !Guid.TryParse(definition.ScenarioId, out _)
            || string.IsNullOrWhiteSpace(definition.Provider)
            || string.IsNullOrWhiteSpace(definition.Symbol)
            || string.IsNullOrWhiteSpace(definition.AdjustmentMode)
            || string.IsNullOrWhiteSpace(definition.Market)
            || string.IsNullOrWhiteSpace(definition.CalendarVersion)
            || definition.RequiredBars is < 2 or > MarketDataExecutionEvidenceLimits.MaximumBars)
            return "acceptance-definition-invalid";
        return null;
    }

    public static string? PlanError(ScriptedBrokerPlan plan)
    {
        if (plan.ContractVersion != TradingCoreAcceptanceVersions.Current)
            return "unsupported-contract";
        if (!TradingCoreAcceptanceScenarioCatalog.IsRequired(plan.ScenarioCode)
            || !Guid.TryParse(plan.ScenarioId, out _)
            || plan.VirtualStartUtc.Kind != DateTimeKind.Utc
            || plan.Steps.Any(step => !ScriptedBrokerActions.All.Contains(step.Action)
                || step.CallOrdinal < 0)
            || plan.PlanHash != TradingCoreAcceptanceIdentity.Plan(plan))
            return "acceptance-plan-invalid";
        return null;
    }

    public static string? ManifestError(AcceptanceManifestV1 manifest)
    {
        if (manifest.ContractVersion != TradingCoreAcceptanceVersions.Current
            || manifest.EnvironmentClass != "IsolatedAcceptance"
            || string.IsNullOrWhiteSpace(manifest.RepositoryCommit)
            || string.IsNullOrWhiteSpace(manifest.BuildId))
            return "unsupported-contract";
        var codes = manifest.Scenarios.Select(value => value.ScenarioCode).ToArray();
        if (TradingCoreAcceptanceScenarioCatalog.Required.Any(required =>
                codes.Count(code => code == required) != 1)
            || !HasHashes(manifest.ImageDigests, TradingCoreAcceptanceImageCatalog.Required)
            || !HasHashes(manifest.SharedAssemblyHashes,
                TradingCoreAcceptanceAssemblyCatalog.Required)
            || manifest.Passed != manifest.Scenarios.All(value => value.Passed)
            || manifest.ManifestId != TradingCoreAcceptanceIdentity.Manifest(manifest))
            return "acceptance-manifest-invalid";
        return null;
    }

    private static bool HasHashes(IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> required) =>
        required.All(values.ContainsKey) && values.Values.All(value =>
        {
            if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                value = value[7..];
            return value.Length >= 32 && value.All(Uri.IsHexDigit);
        });


    public static string? FixtureError(AcceptanceScenarioFixture fixture)
    {
        if (fixture is null
            || fixture.ContractVersion != TradingCoreAcceptanceVersions.Current
            || PlanError(fixture.BrokerPlan) is not null
            || fixture.Bootstrap.ScenarioId != fixture.BrokerPlan.ScenarioId
            || fixture.Operations.Count == 0
            || fixture.Assertions.Count == 0
            || fixture.Assertions.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count()
                != fixture.Assertions.Count
            || fixture.Assertions.Any(value =>
                string.IsNullOrWhiteSpace(value.Name)
                || value.Target is not (AcceptanceDriverTargets.TradingCore
                    or AcceptanceDriverTargets.TradingCoreStatus
                    or AcceptanceDriverTargets.BrokerControl)
                || !value.JsonPointer.StartsWith('/'))
            || fixture.ExpectedStateHash
                != TradingCoreAcceptanceIdentity.ExpectedState(fixture.Assertions)
            || fixture.Operations.Any(value =>
                string.IsNullOrWhiteSpace(value.OperationId)
                || !AcceptanceDriverTargets.All.Contains(value.Target)
                || value.MaxAttempts is < 1 or > 100
                || value.ExpectedStatus is < 100 or > 599)
            || fixture.FixtureHash != TradingCoreAcceptanceIdentity.Fixture(fixture))
            return "acceptance-fixture-invalid";
        return null;
    }
}

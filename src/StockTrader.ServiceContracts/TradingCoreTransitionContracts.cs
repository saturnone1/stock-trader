namespace StockTrader.ServiceContracts.TradingCore;

public static class TradingControlContractVersions
{
    public const int Current = 2;
}

public static class AuthorityTransitionDirections
{
    public const string Cutover = "Cutover";
    public const string Rollback = "Rollback";
    public static IReadOnlySet<string> All { get; } = Set(Cutover, Rollback);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public static class AuthorityOwners
{
    public const string Edge = "Edge";
    public const string TradingCore = "TradingCore";
    public static IReadOnlySet<string> All { get; } = Set(Edge, TradingCore);

    public static string ForMode(TradingAuthorityMode mode) => mode switch
    {
        TradingAuthorityMode.Local or TradingAuthorityMode.Projection or TradingAuthorityMode.Shadow
            => Edge,
        TradingAuthorityMode.Remote => TradingCore,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public static class AuthorityTransitionPhases
{
    public const string Requested = "Requested";
    public const string Quiescing = "Quiescing";
    public const string Draining = "Draining";
    public const string Reconciled = "Reconciled";
    public const string Committing = "Committing";
    public const string Verifying = "Verifying";
    public const string ReadyToRelease = "ReadyToRelease";
    public const string Completed = "Completed";
    public const string Blocked = "Blocked";

    public static IReadOnlySet<string> All { get; } = Set(
        Requested, Quiescing, Draining, Reconciled, Committing,
        Verifying, ReadyToRelease, Completed, Blocked);

    public static bool IsTerminal(string phase) => phase == Completed;

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public static class AuthorityCommandAcceptanceStates
{
    public const string Open = "Open";
    public const string Fenced = "Fenced";
}

public static class AuthorityTransitionOutcomes
{
    public const string None = "None";
    public const string TargetCommitted = "TargetCommitted";
    public const string SourceRetained = "SourceRetained";
}

public static class AuthorityTransitionOperations
{
    public const string Create = "Create";
    public const string Quiesce = "Quiesce";
    public const string Drain = "Drain";
    public const string Reconcile = "Reconcile";
    public const string Commit = "Commit";
    public const string CompleteVerification = "CompleteVerification";
    public const string Release = "Release";
    public const string Abort = "Abort";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Create, Quiesce, Drain, Reconcile, Commit, CompleteVerification, Release, Abort],
        StringComparer.Ordinal);
}

public static class AuthorityStopRequiredActions
{
    public const string BlockStart = "BlockStart";
    public const string FenceAndPause = "FenceAndPause";
    public const string AbortBeforeCommit = "AbortBeforeCommit";
    public const string ReconcileOnly = "ReconcileOnly";
    public const string RejectManifest = "RejectManifest";
}

public sealed record TradingControlOperation(
    int ContractVersion,
    string OperationId,
    string PayloadHash,
    string CorrelationId,
    string? CausationId,
    DateTime ObservedAtUtc);

public sealed record AuthorityStopReason(
    string Code,
    string Category,
    string RequiredAction,
    DateTime FirstObservedAtUtc,
    DateTime LastObservedAtUtc,
    IReadOnlyList<string> EvidenceReferences);

public sealed record AuthorityTransitionRequest(
    TradingControlOperation Operation,
    string TransitionId,
    string Direction,
    TradingAuthorityMode SourceMode,
    TradingAuthorityMode TargetMode,
    long SourceGeneration,
    long AccountGeneration,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record AuthorityFenceReceipt(
    string Owner,
    long AuthorityGeneration,
    string NewEntryAcceptance,
    string ManualCommandAcceptance,
    string PositionCycle,
    string EntryReconciliation,
    string PositionReconciliation,
    DateTime? LastCompletedPositionBarUtc,
    int UnresolvedIntentCount,
    int UnresolvedBrokerEffectCount,
    long ActivityJournalCount,
    long EnabledConsumerLag,
    string FenceHash);

public sealed record EdgeAuthorityFenceRequest(
    TradingControlOperation Operation,
    string TransitionId,
    long AuthorityGeneration);

public sealed record EdgeAuthorityMirrorRequest(
    TradingControlOperation Operation,
    string TransitionId,
    long AuthorityGeneration,
    string Mode,
    string Owner,
    string AuthorityReceiptHash);

public sealed record AuthorityDrainInventory(
    int UnresolvedIntentCount,
    int UnresolvedBrokerEffectCount,
    int UnprocessedBrokerFillCount,
    long ActivityJournalCount,
    long EnabledConsumerLag,
    DateTime ObservedAtUtc,
    string InventoryHash);

public sealed record AuthorityReconciliationEvidence(
    string SourceStateHash,
    string BrokerReconciliationHash,
    DateTime BrokerReconciledAtUtc,
    int UnresolvedBrokerOrders,
    string TransferId,
    string TransferHash);

public sealed record AuthorityCapabilityReceipt(
    string Owner,
    string RuntimeProfile,
    string ImageDigest,
    string AssemblyInventoryHash,
    string ServiceInventoryHash,
    string SecretReferenceHash,
    string NetworkPolicyHash,
    bool HasFinancialWriter,
    bool HasBrokerAdapter,
    bool HasBrokerSecret,
    bool HasBrokerEgress,
    DateTime ObservedAtUtc,
    string ReceiptHash);

public sealed record AuthorityTransitionStepRequest(
    TradingControlOperation Operation,
    string TransitionId,
    string Step,
    string ExpectedPhase,
    AuthorityFenceReceipt? SourceFence,
    AuthorityFenceReceipt? TargetFence,
    AuthorityDrainInventory? DrainInventory,
    AuthorityReconciliationEvidence? Reconciliation,
    AuthorityCapabilityReceipt? SourceCapability,
    AuthorityCapabilityReceipt? TargetCapability,
    IReadOnlyList<string> EvidenceReferences);

public sealed record AuthorityTransitionView(
    int ContractVersion,
    string TransitionId,
    string Direction,
    TradingAuthorityMode SourceMode,
    TradingAuthorityMode TargetMode,
    string SourceOwner,
    string TargetOwner,
    long SourceGeneration,
    long ReservedGeneration,
    string Phase,
    string CommandAcceptance,
    string SourceStateHash,
    string BrokerReconciliationHash,
    long AccountGeneration,
    DateTime StartedAtUtc,
    DateTime ExpiresAtUtc,
    string LastOperationId,
    string Outcome,
    IReadOnlyList<AuthorityStopReason> StopReasons);

public sealed record AuthorityTransitionReceipt(
    int ContractVersion,
    string OperationId,
    string PayloadHash,
    string TransitionId,
    string Phase,
    string Outcome,
    long EffectiveGeneration,
    bool AlreadyApplied,
    DateTime RecordedAtUtc);

public sealed record TradingAuthorityV2View(
    int ContractVersion,
    TradingAuthorityMode Mode,
    string Owner,
    long Generation,
    string CommandAcceptance,
    string? ActiveTransitionId,
    string? ActiveTransitionPhase);

public static class TradingControlIdentity
{
    public static string Transition(AuthorityTransitionRequest request) =>
        CanonicalJsonHash.Compute(request, nameof(TradingControlOperation.PayloadHash));

    public static string Step(AuthorityTransitionStepRequest request) =>
        CanonicalJsonHash.Compute(request, nameof(TradingControlOperation.PayloadHash));

    public static string EdgeFence(EdgeAuthorityFenceRequest request) =>
        CanonicalJsonHash.Compute(request, nameof(TradingControlOperation.PayloadHash));

    public static string EdgeMirror(EdgeAuthorityMirrorRequest request) =>
        CanonicalJsonHash.Compute(request, nameof(TradingControlOperation.PayloadHash));

    public static string Fence(AuthorityFenceReceipt receipt) =>
        CanonicalJsonHash.Compute(receipt, nameof(AuthorityFenceReceipt.FenceHash));

    public static string Drain(AuthorityDrainInventory inventory) =>
        CanonicalJsonHash.Compute(inventory, nameof(AuthorityDrainInventory.InventoryHash));

    public static string Capability(AuthorityCapabilityReceipt receipt) =>
        CanonicalJsonHash.Compute(receipt, nameof(AuthorityCapabilityReceipt.ReceiptHash));
}

public static class TradingControlCompatibilityPolicy
{
    public static string? Error(EdgeAuthorityFenceRequest request) =>
        request is null || !Guid.TryParse(request.TransitionId, out _)
            || request.AuthorityGeneration < 1
            ? "invalid-edge-authority-transition"
            : OperationError(request.Operation, TradingControlIdentity.EdgeFence(request));

    public static string? Error(EdgeAuthorityMirrorRequest request) =>
        request is null || !Guid.TryParse(request.TransitionId, out _)
            || request.AuthorityGeneration < 1
            || !Enum.TryParse<TradingAuthorityMode>(request.Mode, false, out var mode)
            || AuthorityOwners.ForMode(mode) != request.Owner
            || string.IsNullOrWhiteSpace(request.AuthorityReceiptHash)
            ? "invalid-edge-authority-mirror"
            : OperationError(request.Operation, TradingControlIdentity.EdgeMirror(request));

    public static string? Error(AuthorityTransitionRequest request, TradingAuthorityV2View current)
    {
        var operationError = OperationError(request.Operation, TradingControlIdentity.Transition(request));
        if (operationError is not null) return operationError;
        if (!Guid.TryParse(request.TransitionId, out _)
            || !AuthorityTransitionDirections.All.Contains(request.Direction)
            || request.SourceGeneration < 1
            || request.AccountGeneration < 1
            || request.StartedAtUtc == default
            || request.ExpiresAtUtc <= request.StartedAtUtc
            || request.ExpiresAtUtc <= request.Operation.ObservedAtUtc)
            return "invalid-transition-request";
        if (request.SourceGeneration != current.Generation
            || request.SourceMode != current.Mode
            || current.CommandAcceptance != AuthorityCommandAcceptanceStates.Open)
            return "authority-generation-mismatch";
        var legal = request.Direction switch
        {
            AuthorityTransitionDirections.Cutover =>
                request.SourceMode == TradingAuthorityMode.Shadow
                && request.TargetMode == TradingAuthorityMode.Remote,
            AuthorityTransitionDirections.Rollback =>
                request.SourceMode == TradingAuthorityMode.Remote
                && request.TargetMode is TradingAuthorityMode.Shadow or TradingAuthorityMode.Projection,
            _ => false,
        };
        return legal ? null : "illegal-authority-transition";
    }

    public static string? Error(AuthorityTransitionStepRequest request)
    {
        var operationError = OperationError(request.Operation, TradingControlIdentity.Step(request));
        if (operationError is not null) return operationError;
        if (!Guid.TryParse(request.TransitionId, out _)
            || !AuthorityTransitionOperations.All.Contains(request.Step)
            || !AuthorityTransitionPhases.All.Contains(request.ExpectedPhase))
            return "invalid-transition-operation";
        return null;
    }

    private static string? OperationError(TradingControlOperation operation, string expectedHash)
    {
        if (operation.ContractVersion != TradingControlContractVersions.Current)
            return "unsupported-contract";
        if (!Guid.TryParse(operation.OperationId, out _)
            || string.IsNullOrWhiteSpace(operation.CorrelationId)
            || operation.ObservedAtUtc.Kind != DateTimeKind.Utc)
            return "invalid-control-operation";
        return string.Equals(operation.PayloadHash, expectedHash, StringComparison.Ordinal)
            ? null
            : "payload-hash-mismatch";
    }
}

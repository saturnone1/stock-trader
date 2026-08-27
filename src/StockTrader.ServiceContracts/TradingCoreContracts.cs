using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.ServiceContracts.TradingCore;

public static class TradingCoreContractVersions
{
    public const int Current = 1;
    public const string Producer = "stocktrader-control-api";
    public const string Service = "stocktrader-trading-core";
}

public enum TradingAuthorityMode
{
    Local,
    Projection,
    Shadow,
    Remote
}

public static class TradingCommandKinds
{
    public const string AcceptEntry = "AcceptEntry";
    public const string ClosePosition = "ClosePosition";
    public const string ReconcileEntry = "ReconcileEntry";
    public const string ReconcilePosition = "ReconcilePosition";
    public const string UpdatePositionState = "UpdatePositionState";
    public const string RecordRecommendation = "RecordRecommendation";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AcceptEntry, ClosePosition, ReconcileEntry, ReconcilePosition, UpdatePositionState,
        RecordRecommendation
    };
}

public static class TradingCommandStatuses
{
    public const string PendingBrokerSubmission = "PendingBrokerSubmission";
    public const string AwaitingBrokerEvidence = "AwaitingBrokerEvidence";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string ReconciliationRequired = "ReconciliationRequired";
}

public static class TradingShadowDispositions
{
    public const string BrokerSubmission = "BrokerSubmission";
    public const string RecommendationOnly = "RecommendationOnly";
    public const string Blocked = "Blocked";
    public const string NoAction = "NoAction";
    public const string PositionCommand = "PositionCommand";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        BrokerSubmission, RecommendationOnly, Blocked, NoAction, PositionCommand
    };
}

public static class TradingPositionActionKinds
{
    public const string FullExit = "FullExit";
    public const string PartialExit = "PartialExit";
    public const string ScaleIn = "ScaleIn";
    public const string ScaleOut = "ScaleOut";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        FullExit, PartialExit, ScaleIn, ScaleOut
    };
}

public static class TradingExecutionArtifactKinds
{
    public const string StrategyDocument = "StrategyDocument";
    public const string BuiltInPattern = "BuiltInPattern";
}

public sealed record TradingLongPositionPolicy(
    int MaxHoldingBars,
    bool EnableTrailingStop,
    decimal TrailingStopAtrMultiplier,
    decimal TrailingActivationR,
    bool EnablePartialProfit,
    decimal PartialProfitRMultiple,
    bool EnableTargetExit,
    bool EnableTimeExit,
    decimal BreakevenAtrMultiplier,
    string StopReason,
    string ProtectedStopReason);

public sealed record TradingCumulativeRsiExitPolicy(
    int RsiPeriod,
    int CumulativePeriod,
    decimal ExitThreshold,
    int LongTrendMovingAveragePeriod);

public sealed record TradingTrendStopPolicy(int MovingAveragePeriod, decimal StopMultiplier);

/// <summary>
/// Normalized, immutable position-management semantics. Trading Core consumes this snapshot and
/// never resolves mutable Edge settings while capital is exposed.
/// </summary>
public sealed record TradingPositionManagementArtifact(
    TradingLongPositionPolicy ExitPolicy,
    int RequiredBars,
    TradingCumulativeRsiExitPolicy? CumulativeRsiExit,
    TradingTrendStopPolicy? TrendStop);

public sealed record TradingStrategyExecutionArtifact(
    int ContractVersion,
    string ArtifactId,
    string Kind,
    string PatternCode,
    StrategyExecutionArtifact? StrategyDocument,
    string BuiltInSettingsJson,
    string DefinitionHash,
    string EngineSemanticsVersion,
    string PatternCatalogVersion,
    string CalendarVersion,
    bool CanOpenPosition,
    bool CanManagePosition,
    TradingPositionManagementArtifact? PositionManagement = null);

public sealed record TradingAuthorityContract(
    int ContractVersion,
    TradingAuthorityMode Mode,
    long Generation,
    string AuthorityId,
    DateTime ActivatedAtUtc,
    string PreviousStateHash,
    string BrokerReconciliationHash,
    DateTime? BrokerReconciledAtUtc,
    int UnresolvedBrokerOrders);

public sealed record TradingCommandEnvelope(
    int ContractVersion,
    string CommandId,
    string CommandKind,
    string PayloadHash,
    string CorrelationId,
    string? CausationId,
    long AuthorityGeneration,
    long AccountGeneration,
    DateTime OccurredAtUtc,
    DateTime ExpiresAtUtc);

public sealed record TradingEntryIntent(
    TradingCommandEnvelope Envelope,
    string SourceSignalId,
    string AccountId,
    string Symbol,
    string Sector,
    string PatternCode,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int ShareQuantity,
    decimal Expectancy,
    TradingStrategyExecutionArtifact ExecutionArtifact,
    MarketDataEvidenceContract MarketDataEvidence);

public sealed record TradingShadowEntryObservation(
    int ContractVersion,
    string DecisionId,
    string PayloadHash,
    DateTime ObservedAtUtc,
    string OrderMode,
    string AuthoritativeDisposition,
    string? AuthoritativeReason,
    TradingEntryIntent Intent);

public sealed record TradingShadowDecisionReceipt(
    int ContractVersion,
    string DecisionId,
    string PayloadHash,
    string AuthoritativeDisposition,
    string CandidateDisposition,
    string? CandidateReason,
    bool IsMatch,
    bool AlreadyApplied,
    DateTime ComparedAtUtc);

public sealed record TradingShadowSummary(
    int ContractVersion,
    long Total,
    long Matches,
    long Mismatches,
    DateTime? LastComparedAtUtc);

public sealed record TradingShadowPositionPolicyState(
    decimal HighSinceEntry,
    decimal StopLossPrice,
    decimal InitialRiskDistance,
    bool BreakevenApplied,
    bool TrailingStopActivated);

public sealed record TradingShadowPositionObservation(
    int ContractVersion,
    string DecisionId,
    string PayloadHash,
    DateTime ObservedAtUtc,
    string PositionId,
    string PositionStateHash,
    string ExpectedExecutionArtifactId,
    MarketDataEvidenceContract MarketDataEvidence,
    string AuthoritativeDisposition,
    string? AuthoritativeAction,
    int? AuthoritativeQuantity,
    string? AuthoritativeReason,
    TradingShadowPositionPolicyState AuthoritativePolicyState,
    string CandidateDisposition,
    string? CandidateAction,
    int? CandidateQuantity,
    string? CandidateReason,
    TradingShadowPositionPolicyState CandidatePolicyState);

public sealed record TradingShadowPositionDecisionReceipt(
    int ContractVersion,
    string DecisionId,
    string PayloadHash,
    string AuthoritativeDisposition,
    string CandidateDisposition,
    string? AuthoritativeAction,
    string? CandidateAction,
    int? AuthoritativeQuantity,
    int? CandidateQuantity,
    bool IsPolicyStateMatch,
    bool IsMatch,
    bool AlreadyApplied,
    DateTime ComparedAtUtc);

public sealed record TradingRecommendationObservation(
    TradingCommandEnvelope Envelope,
    string SourceSignalId,
    string Symbol,
    string PatternCode,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int ShareQuantity,
    decimal Expectancy,
    TradingStrategyExecutionArtifact ExecutionArtifact,
    MarketDataEvidenceContract MarketDataEvidence);

public sealed record TradingPositionCommand(
    TradingCommandEnvelope Envelope,
    string PositionId,
    string Action,
    int Quantity,
    string Reason,
    string ExpectedExecutionArtifactId,
    MarketDataEvidenceContract MarketDataEvidence,
    int? ScalingRuleIndex = null,
    bool MarksPartialProfit = false,
    TradingShadowPositionPolicyState? EvaluatedPolicyState = null,
    decimal EvaluatedEntryAtr = 0m,
    DateTime EvaluatedThroughBarUtc = default,
    long EvaluatedMarketDataRevision = 0);

public sealed record TradingPositionPolicyStateUpdate(
    TradingCommandEnvelope Envelope,
    string PositionId,
    string ExpectedExecutionArtifactId,
    decimal HighSinceEntry,
    decimal StopLossPrice,
    decimal InitialRiskDistance,
    bool BreakevenApplied,
    bool TrailingStopActivated,
    MarketDataEvidenceContract MarketDataEvidence,
    decimal EntryAtr = 0m,
    DateTime EvaluatedThroughBarUtc = default,
    long EvaluatedMarketDataRevision = 0);

public sealed record TradingCommandReceipt(
    int ContractVersion,
    string CommandId,
    string PayloadHash,
    string Status,
    string? FinancialIdentity,
    string Message,
    DateTime AcceptedAtUtc,
    bool AlreadyAccepted);

public sealed record TradingCommandStatusView(
    int ContractVersion,
    string CommandId,
    string CommandKind,
    string PayloadHash,
    string Status,
    string? BrokerOrderId,
    DateTime AcceptedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record TradingAccountProjection(
    string AccountId,
    string BrokerCode,
    string Environment,
    bool IsEnabled,
    bool IsActive,
    long ConfigurationGeneration);

/// <summary>
/// Sensitive control-plane payload. Callers must never log or persist this record as plaintext.
/// ConfigurationHash contains credential fingerprints, not credential values.
/// </summary>
public sealed record TradingAccountConfiguration(
    string AccountId,
    string BrokerCode,
    string Environment,
    bool IsEnabled,
    bool IsActive,
    string ApiKey,
    string ApiSecret);

public sealed record TradingRiskConfiguration(
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

public sealed record TradingAccountConfigurationSet(
    int ContractVersion,
    long Generation,
    string ConfigurationHash,
    DateTime IssuedAtUtc,
    IReadOnlyList<TradingAccountConfiguration> Accounts,
    TradingRiskConfiguration Risk);

public sealed record TradingAccountConfigurationReceipt(
    int ContractVersion,
    long Generation,
    string ConfigurationHash,
    bool AlreadyApplied);

public sealed record TradingRecommendationProjection(
    string RecommendationId,
    string SourceSignalId,
    string Symbol,
    string PatternCode,
    string? CustomPatternName,
    DateTime GeneratedAtUtc,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int ShareQuantity,
    decimal Expectancy,
    string Mode,
    bool WasExecuted,
    DateTime? EntryRequestedAtUtc,
    string? EntryAccountId,
    string? EntryOrderId,
    string? EntryExecutionNote);

public sealed record TradingScalingProjection(int RuleIndex, int ExecutionCount);

public sealed record TradingPositionExecutionContext(
    TradingStrategyExecutionArtifact ExecutionArtifact,
    MarketDataEvidenceContract EntryMarketDataEvidence);

public sealed record TradingPositionProjection(
    string PositionId,
    string? SourceSignalId,
    string AccountId,
    string Symbol,
    string Sector,
    int Quantity,
    int InitialQuantity,
    decimal EntryPrice,
    decimal CurrentPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    string PatternCode,
    string? CustomPatternName,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal? ExitPrice,
    decimal HighSinceEntry,
    decimal EntryAtr,
    decimal InitialRiskDistance,
    bool BreakevenApplied,
    bool TrailingStopActivated,
    bool PartialProfitTaken,
    DateTime? ExecutionRequestedAtUtc,
    string? ExecutionRequestReason,
    int? ExecutionRequestQuantity,
    bool ExecutionRequestMarksPartialProfit,
    string? ExecutionRequestKind,
    int? ExecutionRequestRuleIndex,
    string? ExecutionOrderId,
    IReadOnlyList<TradingScalingProjection> ScalingExecutions,
    TradingPositionExecutionContext? ExecutionContext,
    string? LastEvaluatedEvidenceId = null,
    DateTime? LastEvaluatedBarUtc = null,
    long LastEvaluatedMarketDataRevision = 0);

public sealed record TradingTradeProjection(
    string TradeId,
    string? SourceSignalId,
    string Symbol,
    string PatternCode,
    string? CustomPatternName,
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    DateTime EntryTimeUtc,
    DateTime ExitTimeUtc,
    decimal PnL,
    decimal PnLPercent,
    string ExitReason);

public sealed record TradingRiskProjection(
    decimal DailyPnL,
    decimal DailyPnLPercent,
    int OpenPositionCount,
    bool IsTradingHalted,
    DateTime ObservedAtUtc);

public sealed record TradingBrokerAccountProjection(
    string AccountId,
    decimal TotalEquity,
    decimal Cash,
    decimal BuyingPower,
    decimal UnrealizedPnL,
    decimal DailyPnL,
    bool IsTradingBlocked,
    DateTime ObservedAtUtc);

public sealed record TradingStateSnapshot(
    int ContractVersion,
    string SnapshotId,
    long SourceGeneration,
    DateTime CapturedAtUtc,
    IReadOnlyList<TradingAccountProjection> Accounts,
    IReadOnlyList<TradingRecommendationProjection> Recommendations,
    IReadOnlyList<TradingPositionProjection> Positions,
    IReadOnlyList<TradingTradeProjection> Trades,
    TradingRiskProjection Risk);

public sealed record TradingActivityEvent(
    int ContractVersion,
    string EventId,
    string EventKind,
    string AggregateId,
    long AggregateVersion,
    string PayloadHash,
    string PayloadJson,
    string CorrelationId,
    string? CausationId,
    long AuthorityGeneration,
    DateTime OccurredAtUtc,
    string Producer);

public sealed record TradingCoreStatus(
    int ContractVersion,
    bool Ready,
    TradingAuthorityMode Mode,
    long AuthorityGeneration,
    long AccountGeneration,
    long InboxCount,
    long OutboxPendingCount,
    string LastSnapshotId,
    DateTime? LastBrokerReconciliationAtUtc,
    string? LastError);

public sealed record TradingCorePortfolioView(
    int ContractVersion,
    long AuthorityGeneration,
    IReadOnlyList<TradingRecommendationProjection> Recommendations,
    IReadOnlyList<TradingPositionProjection> Positions,
    IReadOnlyList<TradingTradeProjection> Trades,
    TradingRiskProjection Risk,
    IReadOnlyList<TradingBrokerAccountProjection> Accounts);

public static class TradingCoreIdentity
{
    public static string Snapshot(TradingStateSnapshot snapshot) => CanonicalJsonHash.Compute(
        snapshot with { SnapshotId = string.Empty }, nameof(TradingStateSnapshot.SnapshotId));

    public static string EntryPayload(TradingEntryIntent intent) => CanonicalJsonHash.Compute(new
    {
        intent.SourceSignalId,
        intent.AccountId,
        intent.Symbol,
        intent.Sector,
        intent.PatternCode,
        intent.CustomPatternName,
        intent.EntryPrice,
        intent.StopLossPrice,
        intent.TargetPrice,
        intent.ShareQuantity,
        intent.Expectancy,
        intent.ExecutionArtifact,
        intent.MarketDataEvidence
    });

    public static string ShadowEntryPayload(TradingShadowEntryObservation observation) =>
        CanonicalJsonHash.Compute(new
        {
            observation.ObservedAtUtc,
            observation.OrderMode,
            observation.AuthoritativeDisposition,
            observation.AuthoritativeReason,
            observation.Intent,
        });

    public static string ShadowPositionPayload(TradingShadowPositionObservation observation) =>
        CanonicalJsonHash.Compute(new
        {
            observation.ObservedAtUtc,
            observation.PositionId,
            observation.PositionStateHash,
            observation.ExpectedExecutionArtifactId,
            observation.MarketDataEvidence,
            observation.AuthoritativeDisposition,
            observation.AuthoritativeAction,
            observation.AuthoritativeQuantity,
            observation.AuthoritativeReason,
            observation.AuthoritativePolicyState,
            observation.CandidateDisposition,
            observation.CandidateAction,
            observation.CandidateQuantity,
            observation.CandidateReason,
            observation.CandidatePolicyState,
        });

    public static string RecommendationPayload(TradingRecommendationObservation observation) =>
        CanonicalJsonHash.Compute(new
        {
            observation.SourceSignalId,
            observation.Symbol,
            observation.PatternCode,
            observation.CustomPatternName,
            observation.EntryPrice,
            observation.StopLossPrice,
            observation.TargetPrice,
            observation.ShareQuantity,
            observation.Expectancy,
            observation.ExecutionArtifact,
            observation.MarketDataEvidence,
        });

    public static string PositionPayload(TradingPositionCommand command) =>
        CanonicalJsonHash.Compute(new
        {
            command.PositionId,
            command.Action,
            command.Quantity,
            command.Reason,
            command.ExpectedExecutionArtifactId,
            command.MarketDataEvidence,
            command.ScalingRuleIndex,
            command.MarksPartialProfit,
            command.EvaluatedPolicyState,
            command.EvaluatedEntryAtr,
            command.EvaluatedThroughBarUtc,
            command.EvaluatedMarketDataRevision,
        });

    public static string PositionStatePayload(TradingPositionPolicyStateUpdate update) =>
        CanonicalJsonHash.Compute(new
        {
            update.PositionId,
            update.ExpectedExecutionArtifactId,
            update.HighSinceEntry,
            update.StopLossPrice,
            update.InitialRiskDistance,
            update.BreakevenApplied,
            update.TrailingStopActivated,
            update.MarketDataEvidence,
            update.EntryAtr,
            update.EvaluatedThroughBarUtc,
            update.EvaluatedMarketDataRevision,
        });

    public static string AccountConfiguration(TradingAccountConfigurationSet configuration) =>
        CanonicalJsonHash.Compute(new
        {
            configuration.ContractVersion,
            configuration.Generation,
            configuration.IssuedAtUtc,
            configuration.Risk,
            Accounts = configuration.Accounts.Select(account => new
            {
                account.AccountId,
                account.BrokerCode,
                account.Environment,
                account.IsEnabled,
                account.IsActive,
                ApiKeyHash = SecretFingerprint(account.ApiKey),
                ApiSecretHash = SecretFingerprint(account.ApiSecret),
            }).OrderBy(account => account.AccountId, StringComparer.Ordinal)
        });

    private static string SecretFingerprint(string value) => CanonicalJsonHash.Compute(value);
}

public static class TradingCoreCompatibilityPolicy
{
    public static string? Error(TradingShadowEntryObservation observation,
        TradingAuthorityContract authority, long currentAccountGeneration, DateTime receivedAtUtc)
    {
        if (observation.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (authority.Mode != TradingAuthorityMode.Shadow)
            return "shadow-authority-not-active";
        if (string.IsNullOrWhiteSpace(observation.DecisionId)
            || !string.Equals(observation.DecisionId, $"shadow:{observation.PayloadHash}",
                StringComparison.Ordinal)
            || observation.AuthoritativeDisposition is not (
                TradingShadowDispositions.BrokerSubmission
                or TradingShadowDispositions.RecommendationOnly
                or TradingShadowDispositions.Blocked)
            || observation.ObservedAtUtc > receivedAtUtc
            || observation.Intent is null)
            return "invalid-shadow-observation";
        var intentError = Error(observation.Intent,
            authority with { Mode = TradingAuthorityMode.Remote },
            currentAccountGeneration, observation.ObservedAtUtc);
        if (intentError is not null) return intentError;
        return string.Equals(observation.PayloadHash,
            TradingCoreIdentity.ShadowEntryPayload(observation), StringComparison.Ordinal)
            ? null : "shadow-payload-hash-mismatch";
    }

    public static string? Error(TradingShadowPositionObservation observation,
        TradingAuthorityContract authority, DateTime receivedAtUtc)
    {
        if (observation.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (authority.Mode != TradingAuthorityMode.Shadow)
            return "shadow-authority-not-active";
        if (string.IsNullOrWhiteSpace(observation.DecisionId)
            || !string.Equals(observation.DecisionId, $"shadow-position:{observation.PayloadHash}",
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(observation.PositionId)
            || string.IsNullOrWhiteSpace(observation.PositionStateHash)
            || string.IsNullOrWhiteSpace(observation.ExpectedExecutionArtifactId)
            || observation.ObservedAtUtc > receivedAtUtc
            || observation.MarketDataEvidence is null
            || MarketDataEvidenceError(observation.MarketDataEvidence) is not null
            || !ValidPositionShadowPolicyState(observation.AuthoritativePolicyState)
            || !ValidPositionShadowPolicyState(observation.CandidatePolicyState)
            || !ValidPositionShadowDecision(observation.AuthoritativeDisposition,
                observation.AuthoritativeAction, observation.AuthoritativeQuantity)
            || !ValidPositionShadowDecision(observation.CandidateDisposition,
                observation.CandidateAction, observation.CandidateQuantity))
            return "invalid-shadow-position-observation";
        return string.Equals(observation.PayloadHash,
            TradingCoreIdentity.ShadowPositionPayload(observation), StringComparison.Ordinal)
            ? null : "shadow-position-payload-hash-mismatch";
    }

    public static string? Error(TradingAuthorityContract authority)
    {
        if (authority.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (authority.Generation < 1 || string.IsNullOrWhiteSpace(authority.AuthorityId))
            return "invalid-authority";
        if (authority.Mode == TradingAuthorityMode.Remote
            && (string.IsNullOrWhiteSpace(authority.PreviousStateHash)
                || string.IsNullOrWhiteSpace(authority.BrokerReconciliationHash)
                || authority.BrokerReconciledAtUtc is null
                || authority.UnresolvedBrokerOrders != 0))
            return "incomplete-cutover-reconciliation";
        return null;
    }

    public static string? Error(TradingEntryIntent intent, TradingAuthorityContract authority,
        long currentAccountGeneration, DateTime observedAtUtc)
    {
        var envelopeError = EnvelopeError(intent.Envelope, TradingCommandKinds.AcceptEntry,
            authority, currentAccountGeneration, observedAtUtc);
        if (envelopeError is not null) return envelopeError;
        if (string.IsNullOrWhiteSpace(intent.SourceSignalId)
            || string.IsNullOrWhiteSpace(intent.AccountId)
            || string.IsNullOrWhiteSpace(intent.Symbol)
            || string.IsNullOrWhiteSpace(intent.PatternCode)
            || intent.ExecutionArtifact is null
            || intent.MarketDataEvidence is null
            || MarketDataEvidenceError(intent.MarketDataEvidence) is not null
            || TradingExecutionArtifactPolicy.Error(intent.ExecutionArtifact) is not null
            || intent.ExecutionArtifact.PositionManagement is null
            || !intent.ExecutionArtifact.CanOpenPosition
            || !string.Equals(intent.ExecutionArtifact.PatternCode, intent.PatternCode,
                StringComparison.Ordinal)
            || !string.Equals(intent.ExecutionArtifact.CalendarVersion,
                intent.MarketDataEvidence.CalendarVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(intent.MarketDataEvidence?.EvidenceId)
            || !string.Equals(intent.MarketDataEvidence.TimeFrame, "Daily",
                StringComparison.Ordinal)
            || !intent.MarketDataEvidence.IsComplete
            || intent.MarketDataEvidence.LastBarUtc is null
            || intent.MarketDataEvidence.LastBarUtc > intent.Envelope.OccurredAtUtc
            || intent.EntryPrice <= 0 || intent.StopLossPrice <= 0 || intent.TargetPrice <= 0
            || intent.ShareQuantity <= 0)
            return "invalid-entry-intent";
        return string.Equals(intent.Envelope.PayloadHash, TradingCoreIdentity.EntryPayload(intent),
            StringComparison.Ordinal) ? null : "payload-hash-mismatch";
    }

    public static string? Error(TradingPositionCommand command, TradingAuthorityContract authority,
        long currentAccountGeneration, DateTime observedAtUtc)
    {
        var envelopeError = EnvelopeError(command.Envelope, command.Envelope.CommandKind,
            authority, currentAccountGeneration, observedAtUtc);
        if (envelopeError is not null) return envelopeError;
        if (command.Envelope.CommandKind is not (TradingCommandKinds.ClosePosition
            or TradingCommandKinds.ReconcilePosition)
            || string.IsNullOrWhiteSpace(command.PositionId)
            || !TradingPositionActionKinds.All.Contains(command.Action)
            || command.Quantity <= 0
            || string.IsNullOrWhiteSpace(command.Reason)
            || string.IsNullOrWhiteSpace(command.ExpectedExecutionArtifactId)
            || command.MarketDataEvidence is null
            || MarketDataEvidenceError(command.MarketDataEvidence) is not null
            || !command.MarketDataEvidence.IsComplete
            || command.MarketDataEvidence.LastBarUtc is null
            || command.MarketDataEvidence.LastBarUtc > command.Envelope.OccurredAtUtc
            || command.EvaluatedEntryAtr < 0
            || (command.EvaluatedPolicyState is { } state
                && (!ValidPositionShadowPolicyState(state)
                    || command.EvaluatedThroughBarUtc == default
                    || command.EvaluatedThroughBarUtc > command.MarketDataEvidence.LastBarUtc
                    || command.EvaluatedMarketDataRevision <= 0
                    || command.EvaluatedMarketDataRevision > command.MarketDataEvidence.Revision)))
            return "invalid-position-command";
        return string.Equals(command.Envelope.PayloadHash,
            TradingCoreIdentity.PositionPayload(command), StringComparison.Ordinal)
            ? null : "payload-hash-mismatch";
    }

    public static string? Error(TradingRecommendationObservation observation,
        TradingAuthorityContract authority,
        long currentAccountGeneration,
        DateTime observedAtUtc)
    {
        var envelopeError = EnvelopeError(observation.Envelope,
            TradingCommandKinds.RecordRecommendation, authority, currentAccountGeneration,
            observedAtUtc);
        if (envelopeError is not null) return envelopeError;
        if (string.IsNullOrWhiteSpace(observation.SourceSignalId)
            || string.IsNullOrWhiteSpace(observation.Symbol)
            || string.IsNullOrWhiteSpace(observation.PatternCode)
            || observation.EntryPrice <= 0
            || observation.StopLossPrice <= 0
            || observation.TargetPrice <= 0
            || observation.ShareQuantity <= 0
            || observation.ExecutionArtifact is null
            || TradingExecutionArtifactPolicy.Error(observation.ExecutionArtifact) is not null
            || observation.MarketDataEvidence is null
            || MarketDataEvidenceError(observation.MarketDataEvidence) is not null
            || !observation.MarketDataEvidence.IsComplete
            || observation.MarketDataEvidence.LastBarUtc is null
            || observation.MarketDataEvidence.LastBarUtc > observation.Envelope.OccurredAtUtc)
            return "invalid-recommendation-observation";
        return string.Equals(observation.Envelope.PayloadHash,
            TradingCoreIdentity.RecommendationPayload(observation), StringComparison.Ordinal)
            ? null : "payload-hash-mismatch";
    }

    public static string? Error(TradingPositionPolicyStateUpdate update,
        TradingAuthorityContract authority,
        long currentAccountGeneration,
        DateTime observedAtUtc)
    {
        var envelopeError = EnvelopeError(update.Envelope,
            TradingCommandKinds.UpdatePositionState, authority, currentAccountGeneration,
            observedAtUtc);
        if (envelopeError is not null) return envelopeError;
        if (string.IsNullOrWhiteSpace(update.PositionId)
            || string.IsNullOrWhiteSpace(update.ExpectedExecutionArtifactId)
            || update.HighSinceEntry <= 0
            || update.StopLossPrice <= 0
            || update.InitialRiskDistance <= 0
            || update.MarketDataEvidence is null
            || MarketDataEvidenceError(update.MarketDataEvidence) is not null
            || !update.MarketDataEvidence.IsComplete
            || update.MarketDataEvidence.LastBarUtc is null
            || update.MarketDataEvidence.LastBarUtc > update.Envelope.OccurredAtUtc
            || update.EntryAtr < 0
            || update.EvaluatedThroughBarUtc == default
            || update.EvaluatedThroughBarUtc > update.MarketDataEvidence.LastBarUtc
            || update.EvaluatedMarketDataRevision <= 0
            || update.EvaluatedMarketDataRevision > update.MarketDataEvidence.Revision)
            return "invalid-position-state-update";
        return string.Equals(update.Envelope.PayloadHash,
            TradingCoreIdentity.PositionStatePayload(update), StringComparison.Ordinal)
            ? null : "payload-hash-mismatch";
    }

    public static string? Error(TradingStateSnapshot snapshot)
    {
        if (snapshot.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (snapshot.SourceGeneration < 1 || snapshot.CapturedAtUtc == default)
            return "invalid-snapshot";
        if (HasDuplicate(snapshot.Accounts.Select(item => item.AccountId))
            || HasDuplicate(snapshot.Recommendations.Select(item => item.RecommendationId))
            || HasDuplicate(snapshot.Positions.Select(item => item.PositionId))
            || HasDuplicate(snapshot.Trades.Select(item => item.TradeId)))
            return "duplicate-financial-identity";
        if (snapshot.Positions.Any(position => position.ExecutionContext is { } context
            && (TradingExecutionArtifactPolicy.Error(context.ExecutionArtifact) is not null
                || MarketDataEvidenceError(context.EntryMarketDataEvidence) is not null
                || !string.Equals(context.ExecutionArtifact.PatternCode,
                    position.PatternCode, StringComparison.Ordinal)
                || !string.Equals(context.EntryMarketDataEvidence.Symbol,
                    position.Symbol, StringComparison.OrdinalIgnoreCase))))
            return "invalid-position-execution-context";
        return string.Equals(snapshot.SnapshotId, TradingCoreIdentity.Snapshot(snapshot),
            StringComparison.Ordinal) ? null : "snapshot-hash-mismatch";
    }

    public static string? Error(TradingAccountConfigurationSet configuration)
    {
        if (configuration.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (configuration.Generation < 1 || configuration.IssuedAtUtc == default)
            return "invalid-account-configuration";
        if (configuration.Risk is null
            || configuration.Risk.RiskPerTradePercent is <= 0 or > 1
            || configuration.Risk.DailyLossLimitPercent is <= 0 or > 1
            || configuration.Risk.MaxTotalPositions <= 0
            || configuration.Risk.MaxPositionsPerSector <= 0
            || configuration.Risk.MaxPositionsPerSector > configuration.Risk.MaxTotalPositions)
            return "invalid-risk-configuration";
        if (HasDuplicate(configuration.Accounts.Select(item => item.AccountId))
            || configuration.Accounts.Count(item => item.IsEnabled && item.IsActive) > 1
            || configuration.Accounts.Any(item => string.IsNullOrWhiteSpace(item.BrokerCode)
                || string.IsNullOrWhiteSpace(item.Environment)
                || (item.IsEnabled && (string.IsNullOrWhiteSpace(item.ApiKey)
                    || string.IsNullOrWhiteSpace(item.ApiSecret)))))
            return "invalid-account-configuration";
        return string.Equals(configuration.ConfigurationHash,
            TradingCoreIdentity.AccountConfiguration(configuration), StringComparison.Ordinal)
            ? null : "account-configuration-hash-mismatch";
    }

    private static string? EnvelopeError(TradingCommandEnvelope envelope, string expectedKind,
        TradingAuthorityContract authority, long accountGeneration, DateTime observedAtUtc)
    {
        if (envelope.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-contract";
        if (!TradingCommandKinds.All.Contains(envelope.CommandKind)
            || envelope.CommandKind != expectedKind
            || string.IsNullOrWhiteSpace(envelope.CommandId)
            || string.IsNullOrWhiteSpace(envelope.PayloadHash)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId))
            return "invalid-command-envelope";
        if (authority.Mode != TradingAuthorityMode.Remote
            || envelope.AuthorityGeneration != authority.Generation)
            return "stale-or-inactive-authority";
        if (envelope.AccountGeneration != accountGeneration)
            return "stale-account-generation";
        if (envelope.OccurredAtUtc > observedAtUtc || envelope.ExpiresAtUtc <= observedAtUtc)
            return "invalid-command-window";
        return null;
    }

    private static bool HasDuplicate(IEnumerable<string> values) => values
        .Any(string.IsNullOrWhiteSpace)
        || values.GroupBy(item => item, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static bool ValidPositionShadowDecision(
        string disposition, string? action, int? quantity) => disposition switch
        {
            TradingShadowDispositions.NoAction => action is null && quantity is null,
            TradingShadowDispositions.PositionCommand =>
                action is not null && TradingPositionActionKinds.All.Contains(action)
                    && quantity is > 0,
            _ => false,
        };

    private static bool ValidPositionShadowPolicyState(
        TradingShadowPositionPolicyState? state) => state is
        {
            HighSinceEntry: >= 0,
            StopLossPrice: >= 0,
            InitialRiskDistance: >= 0,
        };

    private static string? MarketDataEvidenceError(MarketDataEvidenceContract evidence)
    {
        if (evidence.ContractVersion != MarketDataContractVersions.Current
            || string.IsNullOrWhiteSpace(evidence.Provider)
            || string.IsNullOrWhiteSpace(evidence.Symbol)
            || string.IsNullOrWhiteSpace(evidence.ContentHash)
            || evidence.RequestedFromUtc > evidence.RequestedToUtc
            || evidence.FirstBarUtc > evidence.LastBarUtc)
            return "invalid-market-data-evidence";
        var expected = MarketDataContractHash.Evidence(
            evidence.Provider, evidence.Symbol, evidence.TimeFrame, evidence.AdjustmentMode,
            evidence.CalendarVersion, evidence.Revision, evidence.ContentHash);
        return string.Equals(expected, evidence.EvidenceId, StringComparison.Ordinal)
            ? null
            : "market-data-evidence-hash-mismatch";
    }
}

public static class TradingExecutionArtifactPolicy
{
    public static string ComputeDefinitionHash(
        string kind,
        string patternCode,
        StrategyExecutionArtifact? strategy,
        string builtInSettingsJson,
        string calendarVersion) => CanonicalJsonHash.Compute(new
        {
            Kind = kind,
            PatternCode = patternCode,
            StrategyContentHash = strategy?.ContentHash,
            BuiltInSettingsJson = builtInSettingsJson,
            CalendarVersion = calendarVersion,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
        });

    public static string ComputeDefinitionHash(
        string kind,
        string patternCode,
        StrategyExecutionArtifact? strategy,
        string builtInSettingsJson,
        string calendarVersion,
        TradingPositionManagementArtifact positionManagement) => CanonicalJsonHash.Compute(new
        {
            Kind = kind,
            PatternCode = patternCode,
            StrategyContentHash = strategy?.ContentHash,
            BuiltInSettingsJson = builtInSettingsJson,
            CalendarVersion = calendarVersion,
            PositionManagement = positionManagement,
            OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            OptimizationWorkerContractCatalog.PatternCatalogVersion,
        });

    public static string? Error(TradingStrategyExecutionArtifact artifact)
    {
        if (artifact.ContractVersion != TradingCoreContractVersions.Current)
            return "unsupported-execution-artifact-contract";
        if (string.IsNullOrWhiteSpace(artifact.PatternCode)
            || string.IsNullOrWhiteSpace(artifact.ArtifactId)
            || !artifact.CanManagePosition
            || artifact.EngineSemanticsVersion != OptimizationWorkerContractCatalog.EngineSemanticsVersion
            || artifact.PatternCatalogVersion != OptimizationWorkerContractCatalog.PatternCatalogVersion)
            return "incompatible-execution-artifact";
        if (artifact.PositionManagement is { } management
            && (management.RequiredBars < 1
                || management.RequiredBars > MarketDataExecutionEvidenceLimits.MaximumBars
                || management.ExitPolicy is null
                || management.ExitPolicy.MaxHoldingBars < 0
                || management.ExitPolicy.TrailingStopAtrMultiplier < 0
                || management.ExitPolicy.PartialProfitRMultiple < 0
                || management.ExitPolicy.BreakevenAtrMultiplier < 0))
            return "invalid-position-management-artifact";
        if (artifact.Kind == TradingExecutionArtifactKinds.StrategyDocument)
        {
            if (artifact.StrategyDocument is null || artifact.BuiltInSettingsJson != "{}"
                || artifact.StrategyDocument.ContractVersion
                    != OptimizationWorkerContractCatalog.EvaluationInputVersion
                || artifact.StrategyDocument.EngineSemanticsVersion
                    != artifact.EngineSemanticsVersion
                || artifact.StrategyDocument.PatternCatalogVersion
                    != artifact.PatternCatalogVersion
                || artifact.StrategyDocument.CalendarVersion != artifact.CalendarVersion
                || string.IsNullOrWhiteSpace(artifact.StrategyDocument.ContentHash)
                || string.IsNullOrWhiteSpace(artifact.StrategyDocument.StrategyDocumentJson))
                return "invalid-strategy-document-artifact";
        }
        else if (artifact.Kind == TradingExecutionArtifactKinds.BuiltInPattern)
        {
            if (artifact.StrategyDocument is not null
                || string.IsNullOrWhiteSpace(artifact.BuiltInSettingsJson))
                return "invalid-built-in-pattern-artifact";
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(artifact.BuiltInSettingsJson);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return "invalid-built-in-settings";
            }
            catch (System.Text.Json.JsonException) { return "invalid-built-in-settings"; }
        }
        else return "unsupported-execution-artifact-kind";

        var expected = artifact.PositionManagement is null
            ? ComputeDefinitionHash(artifact.Kind, artifact.PatternCode,
                artifact.StrategyDocument, artifact.BuiltInSettingsJson, artifact.CalendarVersion)
            : ComputeDefinitionHash(artifact.Kind, artifact.PatternCode,
                artifact.StrategyDocument, artifact.BuiltInSettingsJson, artifact.CalendarVersion,
                artifact.PositionManagement);
        return artifact.DefinitionHash == expected && artifact.ArtifactId == expected
            ? null : "execution-artifact-hash-mismatch";
    }
}

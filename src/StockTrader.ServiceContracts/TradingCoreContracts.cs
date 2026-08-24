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

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AcceptEntry, ClosePosition, ReconcileEntry, ReconcilePosition
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
    StrategyExecutionArtifact Strategy,
    MarketDataEvidenceContract MarketDataEvidence);

public sealed record TradingPositionCommand(
    TradingCommandEnvelope Envelope,
    string PositionId,
    string Reason);

public sealed record TradingCommandReceipt(
    int ContractVersion,
    string CommandId,
    string PayloadHash,
    string Status,
    string? FinancialIdentity,
    string Message,
    DateTime AcceptedAtUtc,
    bool AlreadyAccepted);

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
    IReadOnlyList<TradingScalingProjection> ScalingExecutions);

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
        intent.Strategy,
        intent.MarketDataEvidence
    });

    public static string PositionPayload(TradingPositionCommand command) =>
        CanonicalJsonHash.Compute(new { command.PositionId, command.Reason });

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
            || intent.Strategy is null
            || intent.MarketDataEvidence is null
            || string.IsNullOrWhiteSpace(intent.Strategy?.ContentHash)
            || string.IsNullOrWhiteSpace(intent.MarketDataEvidence?.EvidenceId)
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
            || string.IsNullOrWhiteSpace(command.Reason))
            return "invalid-position-command";
        return string.Equals(command.Envelope.PayloadHash,
            TradingCoreIdentity.PositionPayload(command), StringComparison.Ordinal)
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
}

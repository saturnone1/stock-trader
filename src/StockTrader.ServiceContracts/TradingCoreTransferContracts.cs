namespace StockTrader.ServiceContracts.TradingCore;

public static class CanonicalFinancialTransferVersions
{
    public const int Current = 2;
}

public sealed record FinancialTransferCompatibility(
    int TransferVersion,
    string SourceSchemaVersion,
    string TargetSchemaVersion,
    string EngineSemanticsVersion,
    string StrategyArtifactVersion,
    string PatternCatalogVersion,
    string CalendarVersion,
    string MarketDataContractVersion);

public sealed record FinancialAccountReference(
    string AccountId,
    string BrokerCode,
    string Environment,
    bool IsEnabled,
    bool IsActive,
    long ConfigurationGeneration,
    string ConfigurationHash);

public sealed record CanonicalFinancialRow(
    string Identity,
    string? SourceIdentity,
    string PayloadHash,
    string PayloadJson);

public sealed record FinancialExecutionIdentity(
    string SourceSignalId,
    string CommandId,
    string ClientOrderId,
    string BrokerOrderId,
    string TerminalStatus,
    string PayloadHash,
    DateTime LastEvidenceAtUtc);

public sealed record FinancialBrokerEvidence(
    string AccountId,
    string Symbol,
    int CanonicalQuantity,
    int BrokerQuantity,
    string ClientOrderId,
    string BrokerOrderId,
    string Side,
    int RequestedQuantity,
    int CumulativeFillQuantity,
    string Status,
    DateTime LastEvidenceAtUtc,
    string EvidenceHash);

public sealed record FinancialRiskState(
    DateOnly TradingDay,
    string EquityBasis,
    string DailyPnl,
    string DailyPnlPercent,
    bool IsTradingHalted,
    DateTime ObservedAtUtc,
    long AccountGeneration,
    string StateHash);

public sealed record FinancialConsumerCursor(
    string ConsumerId,
    long Cursor,
    long Lag,
    bool IsEnabled);

public sealed record FinancialActivityContinuity(
    IReadOnlyDictionary<string, long> AggregateVersions,
    long JournalHighWatermark,
    IReadOnlyList<FinancialConsumerCursor> ConsumerCursors,
    string ContinuityHash);

public sealed record FinancialTransferSection(
    string Name,
    long RowCount,
    string SectionHash);

public sealed record CanonicalFinancialTransferV2(
    int ContractVersion,
    string TransferId,
    string TransferHash,
    string TransitionId,
    string Direction,
    string SourceOwner,
    TradingAuthorityMode SourceMode,
    long SourceGeneration,
    long ReservedTargetGeneration,
    DateTime CapturedAtUtc,
    FinancialTransferCompatibility Compatibility,
    IReadOnlyList<FinancialAccountReference> Accounts,
    IReadOnlyList<CanonicalFinancialRow> Recommendations,
    IReadOnlyList<CanonicalFinancialRow> Positions,
    IReadOnlyList<CanonicalFinancialRow> RealizedTrades,
    IReadOnlyList<FinancialExecutionIdentity> ExecutionIdentities,
    IReadOnlyList<FinancialBrokerEvidence> BrokerEvidence,
    FinancialRiskState Risk,
    FinancialActivityContinuity Activity,
    IReadOnlyList<FinancialTransferSection> Sections);

public sealed record CanonicalFinancialImportReceipt(
    int ContractVersion,
    string TransferId,
    string TransferHash,
    long ReservedGeneration,
    string ImportStateHash,
    bool AlreadyApplied,
    DateTime ImportedAtUtc);

public static class CanonicalFinancialTransferIdentity
{
    public static string Transfer(CanonicalFinancialTransferV2 transfer) =>
        CanonicalJsonHash.Compute(transfer, nameof(CanonicalFinancialTransferV2.TransferHash));

    public static string Rows<T>(IReadOnlyList<T> rows) => CanonicalJsonHash.Compute(rows);

    public static string Activity(FinancialActivityContinuity activity) =>
        CanonicalJsonHash.Compute(activity, nameof(FinancialActivityContinuity.ContinuityHash));

    public static string Risk(FinancialRiskState risk) =>
        CanonicalJsonHash.Compute(risk, nameof(FinancialRiskState.StateHash));
}

public static class CanonicalFinancialTransferPolicy
{
    private static readonly string[] RequiredSections =
    [
        "accounts", "recommendations", "positions", "realizedTrades",
        "executionIdentities", "brokerEvidence", "risk", "activity"
    ];

    public static string? Error(CanonicalFinancialTransferV2 transfer)
    {
        if (transfer.ContractVersion != CanonicalFinancialTransferVersions.Current
            || transfer.Compatibility.TransferVersion != CanonicalFinancialTransferVersions.Current)
            return "unsupported-contract";
        if (!Guid.TryParse(transfer.TransferId, out _)
            || !Guid.TryParse(transfer.TransitionId, out _)
            || !AuthorityTransitionDirections.All.Contains(transfer.Direction)
            || !AuthorityOwners.All.Contains(transfer.SourceOwner)
            || transfer.SourceOwner != AuthorityOwners.ForMode(transfer.SourceMode)
            || transfer.SourceGeneration < 1
            || transfer.ReservedTargetGeneration != transfer.SourceGeneration + 1
            || transfer.CapturedAtUtc.Kind != DateTimeKind.Utc)
            return "invalid-transfer-identity";
        if (!OrderedUnique(transfer.Accounts.Select(value => value.AccountId))
            || !OrderedUnique(transfer.Recommendations.Select(value => value.Identity))
            || !OrderedUnique(transfer.Positions.Select(value => value.Identity))
            || !OrderedUnique(transfer.RealizedTrades.Select(value => value.Identity))
            || !OrderedUnique(transfer.ExecutionIdentities.Select(value => value.CommandId))
            || !OrderedUnique(transfer.BrokerEvidence.Select(value =>
                $"{value.AccountId}|{value.ClientOrderId}|{value.BrokerOrderId}")))
            return "duplicate-financial-identity";
        if (transfer.Accounts.Any(value => value.ConfigurationGeneration < 1
                || string.IsNullOrWhiteSpace(value.ConfigurationHash))
            || transfer.ExecutionIdentities.Any(value =>
                string.IsNullOrWhiteSpace(value.PayloadHash)
                || string.IsNullOrWhiteSpace(value.TerminalStatus))
            || transfer.BrokerEvidence.Any(value => value.CanonicalQuantity != value.BrokerQuantity))
            return "broker-canonical-quantity-divergence";
        if (transfer.Risk.StateHash != CanonicalFinancialTransferIdentity.Risk(transfer.Risk)
            || transfer.Activity.ContinuityHash
                != CanonicalFinancialTransferIdentity.Activity(transfer.Activity))
            return "snapshot-hash-mismatch";
        if (transfer.Activity.ConsumerCursors.Any(value => value.IsEnabled && value.Lag > 0))
            return "activity-consumer-lag-exceeded";

        var expectedSections = new Dictionary<string, (long Count, string Hash)>(StringComparer.Ordinal)
        {
            ["accounts"] = (transfer.Accounts.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.Accounts)),
            ["recommendations"] = (transfer.Recommendations.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.Recommendations)),
            ["positions"] = (transfer.Positions.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.Positions)),
            ["realizedTrades"] = (transfer.RealizedTrades.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.RealizedTrades)),
            ["executionIdentities"] = (transfer.ExecutionIdentities.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.ExecutionIdentities)),
            ["brokerEvidence"] = (transfer.BrokerEvidence.Count,
                CanonicalFinancialTransferIdentity.Rows(transfer.BrokerEvidence)),
            ["risk"] = (1, transfer.Risk.StateHash),
            ["activity"] = (1, transfer.Activity.ContinuityHash),
        };
        if (!transfer.Sections.Select(value => value.Name).SequenceEqual(RequiredSections)
            || transfer.Sections.Any(section =>
                !expectedSections.TryGetValue(section.Name, out var expected)
                || section.RowCount != expected.Count
                || section.SectionHash != expected.Hash))
            return "canonical-import-mismatch";
        return transfer.TransferHash == CanonicalFinancialTransferIdentity.Transfer(transfer)
            ? null
            : "snapshot-hash-mismatch";
    }

    private static bool OrderedUnique(IEnumerable<string> identities)
    {
        string? previous = null;
        foreach (var identity in identities)
        {
            if (string.IsNullOrWhiteSpace(identity)
                || previous is not null
                && string.CompareOrdinal(previous, identity) >= 0)
                return false;
            previous = identity;
        }
        return true;
    }
}

namespace StockTrader.ServiceContracts.Optimization;

public static class OptimizationWorkerContractCatalog
{
    public const int EvaluationInputVersion = 2;
    public const int LeaseVersion = 2;
    public const int HeartbeatVersion = 2;
    public const int ResultVersion = 2;
    public const string EngineSemanticsVersion = "long-position-session-v1";
    public const string IndicatorCatalogVersion = "indicator-catalog-v1";
    public const string PatternCatalogVersion = "pattern-catalog-v1";
    public const string OptimizationCostModelVersion = "adaptive-cost-v1";
    public const string ShadowValidationPurpose = "shadow-contract-validation-v1";
    public const string ShadowComputePurpose = "shadow-optimization-compute-v1";
}

public static class OptimizationWorkerHttpHeaders
{
    public const string Secret = "X-StockTrader-Worker-Key";
    public const string WorkerId = "X-StockTrader-Worker-Id";
}

public sealed record StrategyExecutionArtifact(
    int ContractVersion,
    string StrategyDocumentJson,
    string ContentHash,
    int StrategyDocumentVersion,
    int CompilerSchemaVersion,
    string EngineSemanticsVersion,
    string IndicatorCatalogVersion,
    string PatternCatalogVersion,
    string CalendarVersion,
    string CostModelVersion);

public enum OptimizationDataCompleteness { Unverified, Complete, Partial }

public sealed record OptimizationSymbolDataEvidence(
    string Symbol,
    string TimeFrame,
    string Provider,
    string Market,
    string AdjustmentMode,
    string SessionScope,
    string CalendarVersion,
    DateTime RequestedFrom,
    DateTime RequestedTo,
    DateTime? FirstObservedBar,
    DateTime? LastObservedBar,
    int BarCount,
    OptimizationDataCompleteness Completeness,
    string ContentHash)
{
    public string MarketTimeZoneId { get; init; } = string.Empty;
    public int WarmupCalendarDays { get; init; }
    public int RequiredWarmupBars { get; init; }
}

public sealed record OptimizationDataEvidenceSet(
    int ContractVersion,
    string EvidenceId,
    IReadOnlyList<OptimizationSymbolDataEvidence> Series);

public sealed record OptimizationEvaluationInput(
    int ContractVersion,
    string InputHash,
    string RequestJson,
    StrategyExecutionArtifact Strategy,
    OptimizationDataEvidenceSet DataEvidence,
    OptimizationPreparedDataSet PreparedData);

public sealed record OptimizationWorkLease(
    int ContractVersion,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    DateTime LeasedAt,
    DateTime ExpiresAt,
    OptimizationEvaluationInput Input)
{
    public string Purpose { get; init; } = OptimizationWorkerContractCatalog.ShadowValidationPurpose;
}

public sealed record OptimizationWorkerHeartbeat(
    int ContractVersion,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    string InputHash,
    long TestedCombinations,
    DateTime ObservedAt);

public sealed record OptimizationWorkerResultSubmission(
    int ContractVersion,
    string SubmissionId,
    string LeaseId,
    int JobId,
    long LeaseGeneration,
    long CancellationGeneration,
    string InputHash,
    string ResultHash,
    string ResultJson,
    DateTime CompletedAt);

public sealed record OptimizationWorkerHeartbeatReceipt(
    int ContractVersion,
    bool Continue,
    DateTime LeaseExpiresAt,
    long CancellationGeneration,
    string Reason);

public sealed record OptimizationWorkerResultReceipt(
    int ContractVersion,
    OptimizationResultAcceptance Acceptance);

public sealed record OptimizationWorkerValidationResult(
    int ContractVersion,
    string Purpose,
    string InputHash,
    string StrategyHash,
    string EvidenceId,
    string PreparedDataHash,
    int SeriesCount,
    int BarCount);

public static class OptimizationEvaluationInputIdentity
{
    public static string Compute(
        int contractVersion,
        string requestJson,
        string strategyHash,
        string evidenceId,
        string preparedDataHash) => CanonicalJsonHash.Compute(new
        {
            ContractVersion = contractVersion,
            RequestJson = requestJson,
            StrategyHash = strategyHash,
            EvidenceId = evidenceId,
            PreparedDataHash = preparedDataHash
        });
}

public static class OptimizationLeaseCompatibilityPolicy
{
    public static string? Error(OptimizationWorkLease lease)
    {
        if (lease.ContractVersion != OptimizationWorkerContractCatalog.LeaseVersion
            || lease.Input.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion
            || lease.Input.Strategy.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion
            || lease.Input.DataEvidence.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion
            || lease.Input.PreparedData.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion)
            return "unsupported-contract";
        if (string.IsNullOrWhiteSpace(lease.LeaseId) || lease.JobId <= 0 || lease.LeaseGeneration < 1)
            return "invalid-lease-identity";
        if (lease.Purpose != OptimizationWorkerContractCatalog.ShadowValidationPurpose
            && lease.Purpose != OptimizationWorkerContractCatalog.ShadowComputePurpose)
            return "unsupported-lease-purpose";
        if (lease.ExpiresAt <= lease.LeasedAt)
            return "invalid-lease-window";
        var expected = OptimizationEvaluationInputIdentity.Compute(
            lease.Input.ContractVersion,
            lease.Input.RequestJson,
            lease.Input.Strategy.ContentHash,
            lease.Input.DataEvidence.EvidenceId,
            lease.Input.PreparedData.DataHash);
        if (!string.Equals(expected, lease.Input.InputHash, StringComparison.Ordinal))
            return "input-hash-mismatch";
        var payloadError = OptimizationDataEvidenceCompatibilityPolicy.Error(lease.Input.DataEvidence)
            ?? OptimizationPreparedDataCompatibilityPolicy.Error(lease.Input.PreparedData);
        return payloadError ?? SeriesAlignmentError(
            lease.Input.DataEvidence, lease.Input.PreparedData);
    }

    private static string? SeriesAlignmentError(
        OptimizationDataEvidenceSet evidence,
        OptimizationPreparedDataSet prepared)
    {
        if (evidence.Series.Count != prepared.Series.Count)
            return "data-series-mismatch";
        var claims = evidence.Series.ToDictionary(
            item => $"{item.TimeFrame}|{item.Symbol}",
            StringComparer.OrdinalIgnoreCase);
        return prepared.Series.All(series =>
            claims.TryGetValue($"{series.TimeFrame}|{series.Symbol}", out var claim)
            && claim.BarCount == series.Bars.Count)
            ? null
            : "data-series-mismatch";
    }
}

public enum OptimizationHeartbeatAcceptance
{
    Accepted, UnsupportedContract, StaleLease, CancelledGeneration,
    InputMismatch, LeaseExpired, InvalidProgress
}

public static class OptimizationHeartbeatAcceptancePolicy
{
    public static OptimizationHeartbeatAcceptance Evaluate(
        OptimizationWorkLease lease,
        OptimizationWorkerHeartbeat heartbeat,
        long cancellationGeneration,
        DateTime observedAt)
    {
        if (heartbeat.ContractVersion != OptimizationWorkerContractCatalog.HeartbeatVersion)
            return OptimizationHeartbeatAcceptance.UnsupportedContract;
        if (heartbeat.JobId != lease.JobId || heartbeat.LeaseId != lease.LeaseId
            || heartbeat.LeaseGeneration != lease.LeaseGeneration)
            return OptimizationHeartbeatAcceptance.StaleLease;
        if (heartbeat.CancellationGeneration != cancellationGeneration
            || lease.CancellationGeneration != cancellationGeneration)
            return OptimizationHeartbeatAcceptance.CancelledGeneration;
        if (heartbeat.InputHash != lease.Input.InputHash)
            return OptimizationHeartbeatAcceptance.InputMismatch;
        if (observedAt > lease.ExpiresAt)
            return OptimizationHeartbeatAcceptance.LeaseExpired;
        return heartbeat.TestedCombinations < 0
            ? OptimizationHeartbeatAcceptance.InvalidProgress
            : OptimizationHeartbeatAcceptance.Accepted;
    }
}

public enum OptimizationResultAcceptance
{
    Accepted, Duplicate, UnsupportedContract, StaleLease, CancelledGeneration,
    InputMismatch, LeaseExpired, ResultHashMismatch, InvalidResultPayload
}

public static class OptimizationResultAcceptancePolicy
{
    public static OptimizationResultAcceptance Evaluate(
        OptimizationWorkLease lease,
        OptimizationWorkerResultSubmission submission,
        long cancellationGeneration,
        bool duplicate,
        DateTime observedAt)
    {
        if (submission.ContractVersion != OptimizationWorkerContractCatalog.ResultVersion)
            return OptimizationResultAcceptance.UnsupportedContract;
        if (submission.JobId != lease.JobId || submission.LeaseId != lease.LeaseId
            || submission.LeaseGeneration != lease.LeaseGeneration)
            return OptimizationResultAcceptance.StaleLease;
        if (submission.CancellationGeneration != cancellationGeneration
            || lease.CancellationGeneration != cancellationGeneration)
            return OptimizationResultAcceptance.CancelledGeneration;
        if (submission.InputHash != lease.Input.InputHash)
            return OptimizationResultAcceptance.InputMismatch;
        if (CanonicalJsonHash.Compute(submission.ResultJson) != submission.ResultHash)
            return OptimizationResultAcceptance.ResultHashMismatch;
        if (duplicate)
            return OptimizationResultAcceptance.Duplicate;
        return observedAt > lease.ExpiresAt
            ? OptimizationResultAcceptance.LeaseExpired
            : OptimizationResultAcceptance.Accepted;
    }
}

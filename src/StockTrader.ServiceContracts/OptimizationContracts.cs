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
    string ContentHash);

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
    OptimizationEvaluationInput Input);

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
        return OptimizationDataEvidenceCompatibilityPolicy.Error(lease.Input.DataEvidence)
            ?? OptimizationPreparedDataCompatibilityPolicy.Error(lease.Input.PreparedData);
    }
}

public enum OptimizationResultAcceptance
{
    Accepted, Duplicate, UnsupportedContract, StaleLease, CancelledGeneration,
    InputMismatch, LeaseExpired, ResultHashMismatch
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
        if (observedAt > lease.ExpiresAt)
            return OptimizationResultAcceptance.LeaseExpired;
        if (CanonicalJsonHash.Compute(submission.ResultJson) != submission.ResultHash)
            return OptimizationResultAcceptance.ResultHashMismatch;
        return duplicate ? OptimizationResultAcceptance.Duplicate : OptimizationResultAcceptance.Accepted;
    }
}

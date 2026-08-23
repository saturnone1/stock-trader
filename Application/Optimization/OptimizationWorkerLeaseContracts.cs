using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Application.Optimization;

/// <summary>
/// Strategy Research가 소유하는 원격 최적화 임대 포트입니다. 구현은 임대와 결과를
/// 애플리케이션 저장소에 기록하며 Worker에는 계약 API만 노출합니다.
/// </summary>
public interface IOptimizationWorkerLeaseCoordinator
{
    Task PublishShadowAsync(
        int jobId,
        OptimizationEvaluationInput input,
        DateTime observedAt,
        CancellationToken ct);

    Task<OptimizationRemoteLeasePublication> PublishRemoteAsync(
        int jobId,
        OptimizationEvaluationInput input,
        DateTime observedAt,
        CancellationToken ct);

    Task CancelRemoteAsync(
        int jobId,
        string leaseId,
        DateTime observedAt,
        CancellationToken ct);

    Task<OptimizationWorkLease?> TryLeaseAsync(
        string workerId,
        DateTime observedAt,
        CancellationToken ct);

    Task<OptimizationWorkerHeartbeatReceipt> HeartbeatAsync(
        string workerId,
        OptimizationWorkerHeartbeat heartbeat,
        DateTime observedAt,
        CancellationToken ct);

    Task<OptimizationWorkerResultReceipt> SubmitResultAsync(
        string workerId,
        OptimizationWorkerResultSubmission submission,
        DateTime observedAt,
        CancellationToken ct);
}

public sealed record OptimizationRemoteLeasePublication(
    string LeaseId,
    string InputHash);

public enum OptimizationRemoteCommitOutcome
{
    Waiting,
    Committed,
    AlreadyCommitted,
    JobStopped,
    LeaseCancelled
}

public interface IOptimizationRemoteResultCommitter
{
    Task<OptimizationRemoteCommitOutcome> TryCommitAsync(
        int jobId,
        string leaseId,
        string inputHash,
        DateTime observedAt,
        CancellationToken ct);
}

public sealed record OptimizationShadowComparisonSummary(
    int Awaiting,
    int Matches,
    int Mismatches,
    DateTime? LastComparedAt);

public interface IOptimizationShadowResultCoordinator
{
    Task RecordAuthoritativeAsync(int jobId, DateTime observedAt, CancellationToken ct);

    Task<OptimizationShadowComparisonSummary> GetSummaryAsync(CancellationToken ct);
}

public sealed record OptimizationWorkerLeaseOperationalSummary(
    int Pending,
    int Active,
    int ExpiredActive,
    int Completed,
    int Cancelled,
    int Reclaimed,
    int CanonicalPending,
    int CanonicalActive,
    int CanonicalCompleted,
    int CanonicalCommitted,
    DateTime? OldestPendingAt,
    DateTime? LastCompletedAt);

public interface IOptimizationWorkerLeaseMonitor
{
    Task<OptimizationWorkerLeaseOperationalSummary> GetOperationalSummaryAsync(
        DateTime observedAt,
        CancellationToken ct);
}

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

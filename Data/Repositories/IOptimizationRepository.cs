using StockTrader.Models;
using StockTrader.Application.Optimization;

namespace StockTrader.Data.Repositories;

public interface IOptimizationRepository
{
    // Job CRUD
    Task<OptimizationJob> CreateJobAsync(OptimizationJob job);
    Task<OptimizationJob?> GetJobSummaryAsync(int id);
    Task<OptimizationJobStatus?> GetJobStatusAsync(int id);

    /// <summary>
    /// Priority DESC 순으로 다음 Pending 작업 하나를 조건부 갱신으로 선점한다.
    /// </summary>
    Task<OptimizationJob?> TryClaimNextPendingJobAsync(DateTime observedAt);
    Task UpdateJobAsync(OptimizationJob job);
    Task UpdateJobProgressAsync(
        int id,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? lastProgressAt,
        long? totalCombinations = null);
    Task RequeueContinuousJobAsync(int id, string requestJson);
    // 결과 관리

    /// <summary>
    /// 작업의 상위 결과를 Rank 오름차순으로 반환한다.
    /// </summary>
    Task<List<OptimizationResult>> GetResultsAsync(int jobId, int top = 50);

    /// <summary>
    /// 새 결과의 순위 병합과 다음 실행 체크포인트를 하나의 트랜잭션으로 저장한다.
    /// </summary>
    Task CommitChunkAsync(
        int jobId,
        List<OptimizationResult> newResults,
        int topResultsToKeep,
        string rankBy,
        long testedCombinations,
        int currentChunkIndex,
        DateTime observedAt);

    /// <summary>
    /// 단일 결과를 업서트한다 (OOS 결과 등 사후 업데이트 시 사용).
    /// Id == 0 이면 Insert, 그 외엔 Update.
    /// </summary>
    Task UpsertResultAsync(OptimizationResult result);
    Task UpdateResultOutOfSampleAsync(
        int resultId,
        OptimizationPerformanceMetrics metrics);

}

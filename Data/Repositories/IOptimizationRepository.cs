using StockTrader.Models;
using StockTrader.Application.Optimization;

namespace StockTrader.Data.Repositories;

public interface IOptimizationRepository
{
    // Job CRUD
    Task<OptimizationJob> CreateJobAsync(OptimizationJob job);
    Task<OptimizationJob?> GetJobAsync(int id);
    Task<OptimizationJob?> GetJobSummaryAsync(int id);
    Task<List<OptimizationJob>> GetJobsAsync(OptimizationJobStatus? status = null);
    Task<OptimizationJobStatus?> GetJobStatusAsync(int id);

    /// <summary>
    /// Priority DESC 순으로 다음 Pending 작업을 반환한다.
    /// </summary>
    Task<OptimizationJob?> GetNextPendingJobAsync();
    Task UpdateJobAsync(OptimizationJob job);
    Task UpdateJobProgressAsync(
        int id,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? lastProgressAt,
        long? totalCombinations = null);
    Task RequeueContinuousJobAsync(int id, string requestJson);
    Task DeleteJobAsync(int id);

    // 결과 관리

    /// <summary>
    /// 작업의 상위 결과를 Rank 오름차순으로 반환한다.
    /// </summary>
    Task<List<OptimizationResult>> GetResultsAsync(int jobId, int top = 50);

    /// <summary>
    /// 기존 상위 N개와 newResults를 합쳐 rankBy 기준 상위 topResultsToKeep개만 보존한다.
    /// Rank 번호를 재계산하며, 트랜잭션으로 원자적 실행된다.
    /// </summary>
    Task MergeResultsAsync(int jobId, List<OptimizationResult> newResults, int topResultsToKeep, string rankBy);

    /// <summary>
    /// 단일 결과를 업서트한다 (OOS 결과 등 사후 업데이트 시 사용).
    /// Id == 0 이면 Insert, 그 외엔 Update.
    /// </summary>
    Task UpsertResultAsync(OptimizationResult result);
    Task UpdateResultOutOfSampleAsync(
        int resultId,
        OptimizationPerformanceMetrics metrics);

    // 스타트업 복구

    /// <summary>
    /// 앱 재시작 시 Running 상태로 남겨진 작업을 Pending으로 리셋한다 (비정상 종료 복구).
    /// </summary>
    Task ResetRunningJobsAsync();
}

namespace StockTrader.Application.Optimization;

public enum OptimizationJobControlSignal
{
    Continue,
    Pause,
    Cancel
}

public sealed record StoredOptimizationCandidate(
    int ResultId,
    OptimizeParamSnapshot Parameters);

/// <summary>장기 최적화 작업 실행에 필요한 목적별 영속성 포트입니다.</summary>
public interface IOptimizationJobExecutionStore
{
    Task<OptimizationJobControlSignal> GetControlSignalAsync(int jobId);

    Task SaveProgressAsync(
        int jobId,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? observedAt,
        long? totalCombinations = null);

    Task SaveChunkAsync(
        int jobId,
        IReadOnlyList<OptimizeResultItem> results,
        long testedAtStart,
        long testedCombinations,
        int currentChunkIndex,
        DateTime observedAt,
        int topResultsToKeep,
        string rankBy);

    Task<IReadOnlyList<StoredOptimizationCandidate>> LoadTopCandidatesAsync(
        int jobId,
        int count);

    Task SaveOutOfSampleAsync(
        int resultId,
        OptimizationPerformanceMetrics metrics);
}

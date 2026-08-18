namespace StockTrader.Application.Optimization;

/// <summary>영속 엔티티와 분리된 단일 최적화 작업의 실행 스냅샷입니다.</summary>
public sealed class OptimizationJobExecutionTicket
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string RequestJson { get; init; } = string.Empty;
    public long TotalCombinations { get; set; }
    public long TestedCombinations { get; set; }
    public int CurrentChunkIndex { get; set; }
    public int ChunkSize { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? LastProgressAt { get; set; }
    public decimal? MaxDurationHours { get; init; }
    public long? MaxTestedCombinations { get; init; }
    public string RankBy { get; init; } = "sortinoRatio";
    public int TopResultsToKeep { get; init; }
}

/// <summary>최적화 큐 선택과 실행 상태 전이를 소유하는 애플리케이션 포트입니다.</summary>
public interface IOptimizationJobLifecycle
{
    Task<OptimizationJobExecutionTicket?> TryStartNextAsync(DateTime observedAt);

    Task ApplyDispositionAsync(
        int jobId,
        OptimizationJobExecutionDisposition disposition,
        DateTime observedAt);

    Task ReturnToPendingAsync(int jobId);

    Task MarkFailedAsync(
        int jobId,
        DateTime failedAt,
        string errorMessage);
}

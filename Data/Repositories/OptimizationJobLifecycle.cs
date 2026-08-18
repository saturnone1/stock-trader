using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>최적화 큐와 상태 전이를 SQLite 작업 엔티티에 적용합니다.</summary>
public sealed class OptimizationJobLifecycle : IOptimizationJobLifecycle
{
    private readonly IOptimizationRepository _repository;

    public OptimizationJobLifecycle(IOptimizationRepository repository)
    {
        _repository = repository;
    }

    public async Task<OptimizationJobExecutionTicket?> TryStartNextAsync(DateTime observedAt)
    {
        var job = await _repository.TryClaimNextPendingJobAsync(observedAt);
        if (job is null) return null;
        return ToTicket(job);
    }

    public async Task ApplyDispositionAsync(
        int jobId,
        OptimizationJobExecutionDisposition disposition,
        DateTime observedAt)
    {
        if (disposition == OptimizationJobExecutionDisposition.Paused)
            return;

        var status = disposition switch
        {
            OptimizationJobExecutionDisposition.Completed => OptimizationJobStatus.Completed,
            OptimizationJobExecutionDisposition.Cancelled => OptimizationJobStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };
        await UpdateStateAsync(jobId, status, observedAt, null);
    }

    public Task ReturnToPendingAsync(int jobId) =>
        UpdateStateAsync(jobId, OptimizationJobStatus.Pending, null, null);

    public Task MarkFailedAsync(
        int jobId,
        DateTime failedAt,
        string errorMessage) =>
        UpdateStateAsync(
            jobId, OptimizationJobStatus.Failed, failedAt, errorMessage);

    private async Task UpdateStateAsync(
        int jobId,
        OptimizationJobStatus status,
        DateTime? completedAt,
        string? errorMessage)
    {
        var job = await _repository.GetJobSummaryAsync(jobId);
        if (job is null) return;

        job.Status = status;
        job.CompletedAt = completedAt;
        job.ErrorMessage = errorMessage;
        await _repository.UpdateJobAsync(job);
    }

    private static OptimizationJobExecutionTicket ToTicket(OptimizationJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Priority = job.Priority,
        RequestJson = job.RequestJson,
        TotalCombinations = job.TotalCombinations,
        TestedCombinations = job.TestedCombinations,
        CurrentChunkIndex = job.CurrentChunkIndex,
        ChunkSize = job.ChunkSize,
        StartedAt = job.StartedAt,
        LastProgressAt = job.LastProgressAt,
        MaxDurationHours = job.MaxDurationHours,
        MaxTestedCombinations = job.MaxTestedCombinations,
        RankBy = job.RankBy,
        TopResultsToKeep = job.TopResultsToKeep
    };
}

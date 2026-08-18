using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>최적화 큐 선점과 실행 상태 전이를 SQLite 행에 직접 적용합니다.</summary>
public sealed class OptimizationJobLifecycle : IOptimizationJobLifecycle
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationJobLifecycle(IDbContextFactory<AppDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<OptimizationJobExecutionTicket?> TryStartNextAsync(DateTime observedAt)
    {
        while (true)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var candidateId = await db.OptimizationJobs.AsNoTracking()
                .Where(job => job.Status == OptimizationJobStatus.Pending)
                .OrderByDescending(job => job.Priority)
                .ThenBy(job => job.CreatedAt)
                .Select(job => (int?)job.Id)
                .FirstOrDefaultAsync();
            if (!candidateId.HasValue) return null;

            var claimed = await db.OptimizationJobs
                .Where(job => job.Id == candidateId && job.Status == OptimizationJobStatus.Pending)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.Status, OptimizationJobStatus.Running)
                    .SetProperty(job => job.StartedAt, job => job.StartedAt ?? observedAt));
            if (claimed == 0) continue;

            var job = await db.OptimizationJobs.AsNoTracking()
                .SingleAsync(item => item.Id == candidateId.Value);
            return ToTicket(job);
        }
    }

    public Task ApplyDispositionAsync(
        int jobId,
        OptimizationJobExecutionDisposition disposition,
        DateTime observedAt)
    {
        if (disposition == OptimizationJobExecutionDisposition.Paused)
            return Task.CompletedTask;

        var status = disposition switch
        {
            OptimizationJobExecutionDisposition.Completed => OptimizationJobStatus.Completed,
            OptimizationJobExecutionDisposition.Cancelled => OptimizationJobStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };
        return UpdateStateAsync(jobId, status, observedAt, null);
    }

    public Task ReturnToPendingAsync(int jobId) =>
        UpdateStateAsync(jobId, OptimizationJobStatus.Pending, null, null);

    public Task MarkFailedAsync(int jobId, DateTime failedAt, string errorMessage) =>
        UpdateStateAsync(jobId, OptimizationJobStatus.Failed, failedAt, errorMessage);

    private async Task UpdateStateAsync(
        int jobId,
        OptimizationJobStatus status,
        DateTime? completedAt,
        string? errorMessage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.OptimizationJobs.Where(job =>
                job.Id == jobId && job.Status == OptimizationJobStatus.Running)
            .ExecuteUpdateAsync(update => update
                .SetProperty(job => job.Status, status)
                .SetProperty(job => job.CompletedAt, completedAt)
                .SetProperty(job => job.ErrorMessage, errorMessage));
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

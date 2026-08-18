using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>작업 제어 포트를 SQLite 조건부 갱신으로 구현합니다.</summary>
public sealed class OptimizationJobControlStore : IOptimizationJobControlStore
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationJobControlStore(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<OptimizationJobControlState?> GetStateAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.OptimizationJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => (OptimizationJobStatus?)job.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return state.HasValue ? OptimizationJobStateMapper.ToApplication(state.Value) : null;
    }

    public async Task<bool> TryTransitionAsync(
        int jobId,
        OptimizationJobStateTransition transition,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var from = OptimizationJobStateMapper.ToStorage(transition.From);
        var target = db.OptimizationJobs
            .Where(job => job.Id == jobId && job.Status == from);
        var affected = transition.CompletedAt.HasValue
            ? await target.ExecuteUpdateAsync(
                update => update
                    .SetProperty(
                        job => job.Status,
                        OptimizationJobStateMapper.ToStorage(transition.To))
                    .SetProperty(job => job.CompletedAt, transition.CompletedAt),
                cancellationToken)
            : await target.ExecuteUpdateAsync(
                update => update.SetProperty(
                    job => job.Status,
                    OptimizationJobStateMapper.ToStorage(transition.To)),
                cancellationToken);
        return affected == 1;
    }

    public async Task<int> RecoverInterruptedAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.OptimizationJobs
            .Where(job => job.Status == OptimizationJobStatus.Running)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(job => job.Status, OptimizationJobStatus.Pending)
                    .SetProperty(
                        job => job.CurrentChunkIndex,
                        job => job.CurrentChunkIndex > 0
                            ? job.CurrentChunkIndex - 1
                            : 0),
                cancellationToken);
    }

}

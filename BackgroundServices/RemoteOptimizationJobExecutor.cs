using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;

namespace StockTrader.BackgroundServices;

/// <summary>
/// Strategy Research prepares immutable input and owns the canonical commit, while the independent
/// Worker is the only process that evaluates optimization candidates.
/// </summary>
public sealed class RemoteOptimizationJobExecutor(
    IServiceScopeFactory scopeFactory,
    IOptions<OptimizationWorkerTransportOptions> transport,
    TimeProvider clock,
    ILogger<RemoteOptimizationJobExecutor> logger) : IOptimizationWorkExecutor
{
    private readonly OptimizationWorkerTransportOptions _transport = transport.Value;

    public async Task<OptimizationJobExecutionDisposition> ExecuteAsync(
        OptimizationJobExecutionTicket job,
        CancellationToken ct)
    {
        if (job.MaxDurationHours.HasValue)
            throw new InvalidOperationException(
                "Remote optimization does not accept wall-clock execution limits; use a combination limit.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var preparer = services.GetRequiredService<IOptimizationEvaluationContextPreparer>();
        var leases = services.GetRequiredService<IOptimizationWorkerLeaseCoordinator>();
        var committer = services.GetRequiredService<IOptimizationRemoteResultCommitter>();
        var executionStore = services.GetRequiredService<IOptimizationJobExecutionStore>();
        var request = OptimizeRequestJsonCodec.Deserialize(job.RequestJson)
            ?? throw new InvalidOperationException("Remote optimization request JSON is invalid.");
        request.RankBy = job.RankBy;
        request.MaxResults = job.TopResultsToKeep;
        if (job.MaxTestedCombinations.HasValue)
            request.MaxCombinations = Math.Min(
                request.MaxCombinations,
                checked((int)Math.Min(job.MaxTestedCombinations.Value, int.MaxValue)));

        var preparation = await preparer.PrepareAsync(request, ct);
        if (!preparation.IsSuccess)
            throw new InvalidOperationException(preparation.Message);
        var input = OptimizationEvaluationInputFactory.Create(preparation.Context!);
        var publication = await leases.PublishRemoteAsync(job.Id, input, UtcNow, ct);
        logger.LogInformation(
            "[Optimization] Job {JobId} leased to the remote Worker boundary as {LeaseId}",
            job.Id,
            publication.LeaseId);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var control = await executionStore.GetControlSignalAsync(job.Id);
            if (control == OptimizationJobControlSignal.Pause)
            {
                await leases.CancelRemoteAsync(job.Id, publication.LeaseId, UtcNow, ct);
                return OptimizationJobExecutionDisposition.Paused;
            }
            if (control == OptimizationJobControlSignal.Cancel)
            {
                await leases.CancelRemoteAsync(job.Id, publication.LeaseId, UtcNow, ct);
                return OptimizationJobExecutionDisposition.Cancelled;
            }

            var outcome = await committer.TryCommitAsync(
                job.Id,
                publication.LeaseId,
                publication.InputHash,
                UtcNow,
                ct);
            if (outcome is OptimizationRemoteCommitOutcome.Committed
                or OptimizationRemoteCommitOutcome.AlreadyCommitted)
            {
                logger.LogInformation(
                    "[Optimization] Job {JobId} canonical remote result committed ({Outcome})",
                    job.Id,
                    outcome);
                return OptimizationJobExecutionDisposition.Completed;
            }
            if (outcome == OptimizationRemoteCommitOutcome.JobStopped)
                return await StoppedDispositionAsync(job.Id, executionStore);
            if (outcome == OptimizationRemoteCommitOutcome.LeaseCancelled)
                throw new InvalidOperationException(
                    $"Remote optimization lease {publication.LeaseId} was cancelled unexpectedly.");

            await Task.Delay(
                TimeSpan.FromMilliseconds(_transport.RemotePollMilliseconds),
                clock,
                ct);
        }
    }

    private static async Task<OptimizationJobExecutionDisposition> StoppedDispositionAsync(
        int jobId,
        IOptimizationJobExecutionStore executionStore) =>
        await executionStore.GetControlSignalAsync(jobId) == OptimizationJobControlSignal.Pause
            ? OptimizationJobExecutionDisposition.Paused
            : OptimizationJobExecutionDisposition.Cancelled;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
}

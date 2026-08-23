using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Data.Repositories;

public sealed partial class OptimizationWorkerLeaseCoordinator
{
    public async Task<OptimizationRemoteCommitOutcome> TryCommitAsync(
        int jobId,
        string leaseId,
        string inputHash,
        DateTime observedAt,
        CancellationToken ct)
    {
        var now = Utc(observedAt);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lease = await db.OptimizationWorkerLeases.SingleOrDefaultAsync(
            item => item.LeaseId == leaseId && item.JobId == jobId,
            ct) ?? throw new InvalidOperationException("Remote optimization lease was not found.");
        if (lease.Authority != OptimizationWorkerLeaseAuthority.Canonical
            || lease.InputHash != inputHash)
            throw new InvalidOperationException("Remote optimization lease identity changed.");
        if (lease.CanonicalCommittedAt.HasValue)
        {
            await transaction.RollbackAsync(ct);
            return OptimizationRemoteCommitOutcome.AlreadyCommitted;
        }
        if (lease.Status == OptimizationWorkerLeaseStatus.Cancelled)
        {
            await transaction.RollbackAsync(ct);
            return OptimizationRemoteCommitOutcome.LeaseCancelled;
        }
        if (lease.Status != OptimizationWorkerLeaseStatus.Completed)
        {
            await transaction.RollbackAsync(ct);
            return OptimizationRemoteCommitOutcome.Waiting;
        }

        var job = await db.OptimizationJobs.SingleAsync(item => item.Id == jobId, ct);
        if (job.Status is OptimizationJobStatus.Paused or OptimizationJobStatus.Cancelled)
        {
            await transaction.RollbackAsync(ct);
            return OptimizationRemoteCommitOutcome.JobStopped;
        }
        if (job.Status != OptimizationJobStatus.Running)
            throw new InvalidOperationException(
                $"Remote optimization Job {jobId} is not running ({job.Status}).");

        if (!IsMatchingComputeResult(lease, lease.ResultJson!))
            throw new InvalidOperationException("Remote optimization result failed contract validation.");

        var result = JsonSerializer.Deserialize<OptimizationWorkerComputeResult>(
            lease.ResultJson!, JsonOptions)
            ?? throw new InvalidOperationException("Remote optimization result is empty.");
        var candidates = MapCanonicalCandidates(result, job, now);

        await db.OptimizationResults.Where(item => item.JobId == jobId).ExecuteDeleteAsync(ct);
        db.OptimizationResults.AddRange(candidates);
        job.TotalCombinations = result.TotalCombinations;
        job.TestedCombinations = result.TestedCombinations;
        job.CurrentChunkIndex = 1;
        job.LastProgressAt = now;
        lease.CanonicalCommittedAt = now;
        lease.CanonicalResultHash = OptimizationShadowResultIdentity.Compute(result);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return OptimizationRemoteCommitOutcome.Committed;
    }

    private static IReadOnlyList<OptimizationResult> MapCanonicalCandidates(
        OptimizationWorkerComputeResult result,
        OptimizationJob job,
        DateTime observedAt)
    {
        if (result.Results.Count > job.TopResultsToKeep
            || result.Results.Select(item => item.Rank).Distinct().Count() != result.Results.Count
            || result.Results.OrderBy(item => item.Rank)
                .Select(item => item.Rank)
                .Where((rank, index) => rank != index + 1).Any())
            throw new InvalidOperationException("Remote optimization result ranks are invalid.");

        return result.Results.OrderBy(item => item.Rank).Select((item, index) =>
        {
            var parameters = JsonSerializer.Deserialize<OptimizeParamSnapshot>(
                item.ParametersJson, JsonOptions)
                ?? throw new InvalidOperationException("Remote optimization parameters are empty.");
            return new OptimizationResult
            {
                JobId = job.Id,
                Rank = item.Rank,
                ParamsJson = JsonSerializer.Serialize(parameters, JsonOptions),
                TotalReturn = item.TotalReturn,
                SortinoRatio = item.SortinoRatio,
                SharpeRatio = item.SharpeRatio,
                MaxDrawdown = item.MaxDrawdown,
                WinRate = item.WinRate,
                TotalTrades = item.TotalTrades,
                ProfitFactor = item.ProfitFactor,
                CalmarRatio = item.CalmarRatio,
                AnnualizedReturn = item.AnnualizedReturn,
                OosTotalReturn = item.OosTotalReturn,
                OosSortinoRatio = item.OosSortinoRatio,
                OosSharpeRatio = item.OosSharpeRatio,
                OosMaxDrawdown = item.OosMaxDrawdown,
                OosWinRate = item.OosWinRate,
                OosTotalTrades = item.OosTotalTrades,
                OosProfitFactor = item.OosProfitFactor,
                OosCalmarRatio = item.OosCalmarRatio,
                OosAnnualizedReturn = item.OosAnnualizedReturn,
                // Remote computation returns a ranked final set rather than streaming discovery
                // offsets. Preserve a deterministic rank-relative value for this legacy audit field.
                TestedAtCombination = index,
                DiscoveredAt = observedAt
            };
        }).ToArray();
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Data.Repositories;

public sealed partial class OptimizationWorkerLeaseCoordinator
    : IOptimizationShadowResultCoordinator
{
    public async Task RecordAuthoritativeAsync(
        int jobId,
        DateTime observedAt,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var lease = await db.OptimizationWorkerLeases
            .SingleOrDefaultAsync(item => item.JobId == jobId
                && item.Authority == OptimizationWorkerLeaseAuthority.Shadow, ct);
        if (lease is null) return;
        var job = await db.OptimizationJobs.AsNoTracking()
            .SingleAsync(item => item.Id == jobId, ct);
        var results = await db.OptimizationResults.AsNoTracking()
            .Where(item => item.JobId == jobId)
            .OrderBy(item => item.Rank)
            .ToListAsync(ct);
        var input = DeserializeInput(lease.InputJson);
        var request = OptimizeRequestJsonCodec.Deserialize(input.RequestJson)
            ?? throw new InvalidOperationException("Stored shadow request is invalid.");
        var period = OptimizationJobExecutionPolicy.SplitPeriod(
            request.From, request.To, request.OosPercent);
        var authoritative = new OptimizationWorkerComputeResult(
            OptimizationWorkerContractCatalog.ResultVersion,
            lease.Purpose,
            lease.InputHash,
            checked((int)job.TotalCombinations),
            checked((int)job.TestedCombinations),
            0,
            request.From,
            period.InSampleTo,
            period.HasOutOfSample ? period.OutOfSampleFrom : null,
            period.HasOutOfSample ? period.OutOfSampleTo : null,
            results.Select(ToCandidate).ToArray());
        lease.AuthoritativeResultJson = OptimizationShadowResultIdentity.Serialize(authoritative);
        lease.AuthoritativeResultHash = OptimizationShadowResultIdentity.Compute(authoritative);
        lease.ComparisonStatus = lease.ResultJson is null
            ? OptimizationShadowComparisonStatus.AwaitingWorker
            : Compare(lease, observedAt);
        await db.SaveChangesAsync(ct);
        LogComparison(lease);
    }

    public async Task<OptimizationShadowComparisonSummary> GetSummaryAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var statuses = await db.OptimizationWorkerLeases.AsNoTracking()
            .Where(item => item.Authority == OptimizationWorkerLeaseAuthority.Shadow)
            .GroupBy(item => item.ComparisonStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        var last = await db.OptimizationWorkerLeases.AsNoTracking()
            .MaxAsync(item => item.ComparedAt, ct);
        int Count(OptimizationShadowComparisonStatus status) =>
            statuses.SingleOrDefault(item => item.Status == status)?.Count ?? 0;
        return new OptimizationShadowComparisonSummary(
            statuses.Where(item => item.Status is OptimizationShadowComparisonStatus.AwaitingBoth
                or OptimizationShadowComparisonStatus.AwaitingAuthoritative
                or OptimizationShadowComparisonStatus.AwaitingWorker).Sum(item => item.Count),
            Count(OptimizationShadowComparisonStatus.Match),
            Count(OptimizationShadowComparisonStatus.Mismatch),
            last);
    }

    private async Task CompleteWorkerComparisonAsync(
        AppDbContext db,
        string leaseId,
        DateTime observedAt,
        CancellationToken ct)
    {
        var lease = await db.OptimizationWorkerLeases
            .SingleAsync(item => item.LeaseId == leaseId, ct);
        lease.ComparisonStatus = lease.AuthoritativeResultHash is null
            ? OptimizationShadowComparisonStatus.AwaitingAuthoritative
            : Compare(lease, observedAt);
        await db.SaveChangesAsync(ct);
        LogComparison(lease);
    }

    private static OptimizationWorkerCandidateResult ToCandidate(OptimizationResult item) => new(
        item.Rank, item.ParamsJson, item.TotalReturn, item.SortinoRatio, item.SharpeRatio,
        item.MaxDrawdown, item.WinRate, item.TotalTrades, item.ProfitFactor, item.CalmarRatio,
        item.AnnualizedReturn, item.OosTotalReturn, item.OosSortinoRatio, item.OosSharpeRatio,
        item.OosMaxDrawdown, item.OosWinRate, item.OosTotalTrades, item.OosProfitFactor,
        item.OosCalmarRatio, item.OosAnnualizedReturn);

    private OptimizationShadowComparisonStatus Compare(
        OptimizationWorkerLeaseRecord lease,
        DateTime observedAt)
    {
        var worker = JsonSerializer.Deserialize<OptimizationWorkerComputeResult>(
            lease.ResultJson!, JsonOptions)
            ?? throw new InvalidOperationException("Worker result is empty.");
        var workerHash = OptimizationShadowResultIdentity.Compute(worker);
        lease.ComparedAt = Utc(observedAt);
        lease.ComparisonDetail = JsonSerializer.Serialize(new
        {
            authoritativeHash = lease.AuthoritativeResultHash,
            workerHash
        }, JsonOptions);
        return string.Equals(
            lease.AuthoritativeResultHash, workerHash, StringComparison.Ordinal)
            ? OptimizationShadowComparisonStatus.Match
            : OptimizationShadowComparisonStatus.Mismatch;
    }

    private void LogComparison(OptimizationWorkerLeaseRecord lease)
    {
        if (lease.ComparisonStatus == OptimizationShadowComparisonStatus.Match)
            _logger.LogInformation("Optimization shadow comparison matched for Job {JobId}", lease.JobId);
        else if (lease.ComparisonStatus == OptimizationShadowComparisonStatus.Mismatch)
            _logger.LogError("Optimization shadow comparison mismatched for Job {JobId}: {Detail}",
                lease.JobId, lease.ComparisonDetail);
    }
}

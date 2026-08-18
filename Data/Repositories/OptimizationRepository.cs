using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.Application.Optimization;

namespace StockTrader.Data.Repositories;

public class OptimizationRepository : IOptimizationRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Job CRUD ──────────────────────────────────────────────────────────────

    public async Task<OptimizationJob> CreateJobAsync(OptimizationJob job)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.OptimizationJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<OptimizationJob?> GetJobSummaryAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<OptimizationJobStatus?> GetJobStatusAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs
            .Where(j => j.Id == id)
            .Select(j => (OptimizationJobStatus?)j.Status)
            .FirstOrDefaultAsync();
    }

    public async Task<OptimizationJob?> TryClaimNextPendingJobAsync(DateTime observedAt)
    {
        while (true)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var candidateId = await db.OptimizationJobs
                .AsNoTracking()
                .Where(job => job.Status == OptimizationJobStatus.Pending)
                .OrderByDescending(job => job.Priority)
                .ThenBy(job => job.CreatedAt)
                .Select(job => (int?)job.Id)
                .FirstOrDefaultAsync();
            if (!candidateId.HasValue)
                return null;

            var claimed = await db.OptimizationJobs
                .Where(job =>
                    job.Id == candidateId.Value
                    && job.Status == OptimizationJobStatus.Pending)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.Status, OptimizationJobStatus.Running)
                    .SetProperty(
                        job => job.StartedAt,
                        job => job.StartedAt ?? observedAt));
            if (claimed == 1)
                return await db.OptimizationJobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == candidateId.Value);

            // 다른 워커가 같은 후보를 먼저 선점했다. 남은 큐를 다시 조회한다.
        }
    }

    public async Task UpdateJobAsync(OptimizationJob job)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.OptimizationJobs.Update(job);
        await db.SaveChangesAsync();
    }

    public async Task UpdateJobProgressAsync(
        int id,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? lastProgressAt,
        long? totalCombinations = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.OptimizationJobs.FindAsync(id)
            ?? throw new InvalidOperationException($"OptimizationJob {id}를 찾을 수 없습니다.");

        job.TestedCombinations = testedCombinations;
        job.CurrentChunkIndex = currentChunkIndex;
        job.LastProgressAt = lastProgressAt;

        if (totalCombinations.HasValue && totalCombinations.Value > 0)
            job.TotalCombinations = totalCombinations.Value;

        await db.SaveChangesAsync();
    }

    public async Task RequeueContinuousJobAsync(int id, string requestJson)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.OptimizationJobs
            .Include(j => j.Results)
            .FirstOrDefaultAsync(j => j.Id == id)
            ?? throw new InvalidOperationException($"OptimizationJob {id}를 찾을 수 없습니다.");

        if (job.Results.Count > 0)
            db.OptimizationResults.RemoveRange(job.Results);

        job.RequestJson = requestJson;
        job.Status = OptimizationJobStatus.Pending;
        job.TestedCombinations = 0;
        job.CurrentChunkIndex = 0;
        job.StartedAt = null;
        job.CompletedAt = null;
        job.LastProgressAt = null;
        job.ErrorMessage = null;

        await db.SaveChangesAsync();
    }

    // ── 결과 관리 ─────────────────────────────────────────────────────────────

    public async Task<List<OptimizationResult>> GetResultsAsync(int jobId, int top = 50)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationResults
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderBy(r => r.Rank)
            .Take(top)
            .ToListAsync();
    }

    public async Task CommitChunkAsync(
        int jobId,
        List<OptimizationResult> newResults,
        int topResultsToKeep,
        string rankBy,
        long testedCombinations,
        int currentChunkIndex,
        DateTime observedAt)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        try
        {
            await MergeRankedResultsAsync(
                db, jobId, newResults, topResultsToKeep, rankBy);

            var job = await db.OptimizationJobs.FindAsync(jobId)
                ?? throw new InvalidOperationException(
                    $"OptimizationJob {jobId}를 찾을 수 없습니다.");
            job.TestedCombinations = testedCombinations;
            job.CurrentChunkIndex = currentChunkIndex;
            job.LastProgressAt = observedAt;

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task UpsertResultAsync(OptimizationResult result)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (result.Id == 0)
            db.OptimizationResults.Add(result);
        else
            db.OptimizationResults.Update(result);
        await db.SaveChangesAsync();
    }

    public async Task UpdateResultOutOfSampleAsync(
        int resultId,
        OptimizationPerformanceMetrics metrics)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var result = await db.OptimizationResults.FindAsync(resultId)
            ?? throw new InvalidOperationException(
                $"OptimizationResult {resultId}를 찾을 수 없습니다.");
        result.OosTotalReturn = metrics.TotalReturn;
        result.OosSortinoRatio = metrics.SortinoRatio;
        result.OosSharpeRatio = metrics.SharpeRatio;
        result.OosMaxDrawdown = metrics.MaxDrawdown;
        result.OosWinRate = metrics.WinRate;
        result.OosTotalTrades = metrics.TotalTrades;
        result.OosProfitFactor = metrics.ProfitFactor;
        result.OosCalmarRatio = metrics.CalmarRatio;
        result.OosAnnualizedReturn = metrics.AnnualizedReturn;
        await db.SaveChangesAsync();
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────────────────

    private static async Task MergeRankedResultsAsync(
        AppDbContext db,
        int jobId,
        List<OptimizationResult> newResults,
        int topResultsToKeep,
        string rankBy)
    {
        var existing = await db.OptimizationResults
            .Where(r => r.JobId == jobId)
            .OrderBy(r => r.Rank)
            .Take(topResultsToKeep)
            .ToListAsync();
        var merged = SortByRankBy(
            existing
                .Concat(newResults.Select(result =>
                {
                    result.JobId = jobId;
                    return result;
                }))
                .ToList(),
            rankBy);
        var toKeep = merged.Take(topResultsToKeep).ToList();
        var existingIds = existing.Select(result => result.Id).ToHashSet();
        var removeIds = merged
            .Skip(topResultsToKeep)
            .Where(result => result.Id != 0 && existingIds.Contains(result.Id))
            .Select(result => result.Id)
            .ToHashSet();

        db.OptimizationResults.RemoveRange(
            existing.Where(result => removeIds.Contains(result.Id)));

        for (var index = 0; index < toKeep.Count; index++)
        {
            var result = toKeep[index];
            result.Rank = index + 1;
            if (result.Id == 0)
                db.OptimizationResults.Add(result);
        }
    }

    private static List<OptimizationResult> SortByRankBy(
        List<OptimizationResult> results, string rankBy)
    {
        return rankBy.ToLowerInvariant() switch
        {
            "totalreturn"      => results.OrderByDescending(r => r.TotalReturn).ToList(),
            "sortinoratio"     => results.OrderByDescending(r => r.SortinoRatio).ToList(),
            "sharperatio"      => results.OrderByDescending(r => r.SharpeRatio).ToList(),
            "calmarratio"      => results.OrderByDescending(r => r.CalmarRatio).ToList(),
            "profitfactor"     => results.OrderByDescending(r => r.ProfitFactor).ToList(),
            "winrate"          => results.OrderByDescending(r => r.WinRate).ToList(),
            "annualizedreturn" => results.OrderByDescending(r => r.AnnualizedReturn).ToList(),
            // 기본: SortinoRatio
            _                  => results.OrderByDescending(r => r.SortinoRatio).ToList(),
        };
    }
}

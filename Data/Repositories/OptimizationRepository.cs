using Microsoft.EntityFrameworkCore;
using StockTrader.Models;

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

    public async Task<OptimizationJob?> GetJobAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs
            .AsNoTracking()
            .Include(j => j.Results.OrderBy(r => r.Rank).Take(50))
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<OptimizationJob?> GetJobSummaryAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<List<OptimizationJob>> GetJobsAsync(OptimizationJobStatus? status = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.OptimizationJobs.AsNoTracking();
        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);
        return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
    }

    public async Task<OptimizationJobStatus?> GetJobStatusAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs
            .Where(j => j.Id == id)
            .Select(j => (OptimizationJobStatus?)j.Status)
            .FirstOrDefaultAsync();
    }

    public async Task<OptimizationJob?> GetNextPendingJobAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Priority DESC → CreatedAt ASC (동일 Priority는 먼저 들어온 것 우선)
        return await db.OptimizationJobs
            .AsNoTracking()
            .Where(j => j.Status == OptimizationJobStatus.Pending)
            .OrderByDescending(j => j.Priority)
            .ThenBy(j => j.CreatedAt)
            .FirstOrDefaultAsync();
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

    public async Task DeleteJobAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var job = await db.OptimizationJobs.FindAsync(id);
        if (job != null)
        {
            db.OptimizationJobs.Remove(job);
            await db.SaveChangesAsync();
        }
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

    public async Task MergeResultsAsync(
        int jobId,
        List<OptimizationResult> newResults,
        int topResultsToKeep,
        string rankBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        try
        {
            // 기존 상위 N개 로드 (추적 필요 — 삭제/업데이트 대상)
            var existing = await db.OptimizationResults
                .Where(r => r.JobId == jobId)
                .OrderBy(r => r.Rank)
                .Take(topResultsToKeep)
                .ToListAsync();

            // 기존 + 신규 합치기
            var merged = existing
                .Concat(newResults.Select(r => { r.JobId = jobId; return r; }))
                .ToList();

            // rankBy 기준 내림차순 정렬 (높을수록 좋은 지표)
            merged = SortByRankBy(merged, rankBy);

            // 상위 N개만 보존
            var toKeep = merged.Take(topResultsToKeep).ToList();
            var toRemove = merged.Skip(topResultsToKeep).ToList();

            // 불필요한 결과 삭제 (기존 엔티티만 삭제 — 신규는 Add 전이라 무시)
            var existingIds = new HashSet<int>(existing.Select(r => r.Id));
            var removeIds = toRemove
                .Where(r => r.Id != 0 && existingIds.Contains(r.Id))
                .Select(r => r.Id)
                .ToHashSet();

            if (removeIds.Count > 0)
            {
                var entitiesToRemove = existing.Where(r => removeIds.Contains(r.Id));
                db.OptimizationResults.RemoveRange(entitiesToRemove);
            }

            // Rank 재계산 및 저장
            for (int i = 0; i < toKeep.Count; i++)
            {
                var result = toKeep[i];
                result.Rank = i + 1;

                if (result.Id == 0)
                {
                    // 신규 엔티티 추가
                    db.OptimizationResults.Add(result);
                }
                else if (existingIds.Contains(result.Id))
                {
                    // 기존 엔티티 Rank 업데이트
                    db.OptimizationResults.Update(result);
                }
            }

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

    // ── 스타트업 복구 ─────────────────────────────────────────────────────────

    public async Task ResetRunningJobsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var runningJobs = await db.OptimizationJobs
            .Where(j => j.Status == OptimizationJobStatus.Running)
            .ToListAsync();

        foreach (var job in runningJobs)
        {
            // 진행 중이던 청크는 재실행되어야 하므로 CurrentChunkIndex를 직전 청크로 되돌린다
            if (job.CurrentChunkIndex > 0)
                job.CurrentChunkIndex--;

            job.Status = OptimizationJobStatus.Pending;
        }

        if (runningJobs.Count > 0)
            await db.SaveChangesAsync();
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────────────────

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

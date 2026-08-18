using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>실행 체크포인트와 순위 결과를 하나의 SQLite 트랜잭션으로 저장합니다.</summary>
public sealed class OptimizationJobExecutionStore : IOptimizationJobExecutionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationJobExecutionStore(IDbContextFactory<AppDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<OptimizationJobControlSignal> GetControlSignalAsync(int jobId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.OptimizationJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => (OptimizationJobStatus?)job.Status)
            .SingleOrDefaultAsync() switch
        {
            OptimizationJobStatus.Paused => OptimizationJobControlSignal.Pause,
            OptimizationJobStatus.Cancelled => OptimizationJobControlSignal.Cancel,
            _ => OptimizationJobControlSignal.Continue
        };
    }

    public async Task SaveProgressAsync(
        int jobId,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? observedAt,
        long? totalCombinations = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var affected = totalCombinations is > 0
            ? await db.OptimizationJobs.Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.TestedCombinations, testedCombinations)
                    .SetProperty(job => job.CurrentChunkIndex, currentChunkIndex)
                    .SetProperty(job => job.LastProgressAt, observedAt)
                    .SetProperty(job => job.TotalCombinations, totalCombinations.Value))
            : await db.OptimizationJobs.Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.TestedCombinations, testedCombinations)
                    .SetProperty(job => job.CurrentChunkIndex, currentChunkIndex)
                    .SetProperty(job => job.LastProgressAt, observedAt));
        EnsureJobExists(affected, jobId);
    }

    public async Task SaveChunkAsync(
        int jobId,
        IReadOnlyList<OptimizeResultItem> results,
        long testedAtStart,
        long testedCombinations,
        int currentChunkIndex,
        DateTime observedAt,
        int topResultsToKeep,
        string rankBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var entities = results.Select((result, index) => ToEntity(
                result, jobId, testedAtStart + index, observedAt)).ToList();
            await OptimizationResultPersistence.MergeRankedAsync(
                db, jobId, entities, topResultsToKeep, rankBy);

            var job = await db.OptimizationJobs.FindAsync(jobId)
                ?? throw MissingJob(jobId);
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

    public async Task<IReadOnlyList<StoredOptimizationCandidate>> LoadTopCandidatesAsync(
        int jobId, int count)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stored = await db.OptimizationResults.AsNoTracking()
            .Where(result => result.JobId == jobId)
            .OrderBy(result => result.Rank)
            .Take(count)
            .Select(result => new { result.Id, result.ParamsJson })
            .ToListAsync();
        var candidates = new List<StoredOptimizationCandidate>(stored.Count);
        foreach (var result in stored)
        {
            try
            {
                var parameters = JsonSerializer.Deserialize<OptimizeParamSnapshot>(
                    result.ParamsJson, JsonOptions);
                if (parameters is not null)
                    candidates.Add(new StoredOptimizationCandidate(result.Id, parameters));
            }
            catch (JsonException)
            {
                // 오래된 손상 행은 실행기에서 건너뛰던 기존 호환 동작을 유지합니다.
            }
        }
        return candidates;
    }

    public async Task SaveOutOfSampleAsync(
        int resultId, OptimizationPerformanceMetrics metrics)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var affected = await db.OptimizationResults
            .Where(result => result.Id == resultId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(result => result.OosTotalReturn, metrics.TotalReturn)
                .SetProperty(result => result.OosSortinoRatio, metrics.SortinoRatio)
                .SetProperty(result => result.OosSharpeRatio, metrics.SharpeRatio)
                .SetProperty(result => result.OosMaxDrawdown, metrics.MaxDrawdown)
                .SetProperty(result => result.OosWinRate, metrics.WinRate)
                .SetProperty(result => result.OosTotalTrades, metrics.TotalTrades)
                .SetProperty(result => result.OosProfitFactor, metrics.ProfitFactor)
                .SetProperty(result => result.OosCalmarRatio, metrics.CalmarRatio)
                .SetProperty(result => result.OosAnnualizedReturn, metrics.AnnualizedReturn));
        if (affected == 0)
            throw new InvalidOperationException($"OptimizationResult {resultId}를 찾을 수 없습니다.");
    }

    private static OptimizationResult ToEntity(
        OptimizeResultItem item, int jobId, long testedAtCombination, DateTime discoveredAt) => new()
    {
        JobId = jobId,
        ParamsJson = JsonSerializer.Serialize(item.Params),
        TotalReturn = item.TotalReturn,
        SortinoRatio = item.SortinoRatio,
        SharpeRatio = item.SharpeRatio,
        MaxDrawdown = item.MaxDrawdown,
        WinRate = item.WinRate,
        TotalTrades = item.TotalTrades,
        ProfitFactor = item.ProfitFactor,
        CalmarRatio = item.CalmarRatio,
        AnnualizedReturn = item.AnnualizedReturn,
        TestedAtCombination = testedAtCombination,
        DiscoveredAt = discoveredAt
    };

    private static void EnsureJobExists(int affected, int jobId)
    {
        if (affected == 0) throw MissingJob(jobId);
    }

    private static InvalidOperationException MissingJob(int jobId) =>
        new($"OptimizationJob {jobId}를 찾을 수 없습니다.");
}

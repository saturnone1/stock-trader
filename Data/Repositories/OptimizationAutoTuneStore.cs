using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

public sealed class OptimizationAutoTuneStore : IOptimizationAutoTuneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationAutoTuneStore(IDbContextFactory<AppDbContext> dbFactory) =>
        _dbFactory = dbFactory;

    public async Task<OptimizationAutoTuneJob?> FindJobAsync(
        int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var job = await db.OptimizationJobs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, ct);
        return job is null ? null : new OptimizationAutoTuneJob(
            job.Id,
            OptimizeRequestJsonCodec.Deserialize(job.RequestJson),
            job.AutoApplyBestResult,
            job.ContinuousMode,
            job.RankBy,
            job.TopResultsToKeep,
            job.AutoApplyMinTrades,
            job.AppliedResultCount);
    }

    public async Task<OptimizationPromotionCandidate?> FindCandidateAsync(
        int jobId, int resultId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var result = await db.OptimizationResults.AsNoTracking()
            .SingleOrDefaultAsync(item => item.JobId == jobId && item.Id == resultId, ct);
        return result is null ? null : Map(result);
    }

    public async Task<IReadOnlyList<OptimizationPromotionCandidate>> ListCandidatesAsync(
        int jobId, int count, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var results = await db.OptimizationResults.AsNoTracking()
            .Where(result => result.JobId == jobId)
            .OrderBy(result => result.Rank)
            .Take(count)
            .ToListAsync(ct);
        return results.Select(Map).ToList();
    }

    public async Task<int> RecordApplyOutcomeAsync(
        int jobId,
        int? resultId,
        string message,
        DateTime observedAt,
        bool incrementAppliedCount,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var affected = incrementAppliedCount
            ? await db.OptimizationJobs.Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.LastAutoAppliedAt, observedAt)
                    .SetProperty(job => job.LastAutoAppliedResultId, resultId)
                    .SetProperty(job => job.LastAutoApplyMessage, message)
                    .SetProperty(job => job.AppliedResultCount, job => job.AppliedResultCount + 1), ct)
            : await db.OptimizationJobs.Where(job => job.Id == jobId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(job => job.LastAutoAppliedAt, observedAt)
                    .SetProperty(job => job.LastAutoAppliedResultId, resultId)
                    .SetProperty(job => job.LastAutoApplyMessage, message), ct);
        if (affected == 0) return 0;

        return await db.OptimizationJobs.AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.AppliedResultCount)
            .SingleAsync(ct);
    }

    public async Task<bool> RequeueAsync(
        int jobId, OptimizeRequest nextRequest, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var deleted = await db.OptimizationResults
            .Where(result => result.JobId == jobId)
            .ExecuteDeleteAsync(ct);
        _ = deleted;
        var affected = await db.OptimizationJobs
            .Where(job => job.Id == jobId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(job => job.RequestJson, OptimizeRequestJsonCodec.Serialize(nextRequest))
                .SetProperty(job => job.Status, OptimizationJobStatus.Pending)
                .SetProperty(job => job.TestedCombinations, 0)
                .SetProperty(job => job.CurrentChunkIndex, 0)
                .SetProperty(job => job.StartedAt, (DateTime?)null)
                .SetProperty(job => job.CompletedAt, (DateTime?)null)
                .SetProperty(job => job.LastProgressAt, (DateTime?)null)
                .SetProperty(job => job.ErrorMessage, (string?)null), ct);
        if (affected == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private static OptimizationPromotionCandidate Map(OptimizationResult result) => new(
        result.Id,
        DeserializeParameters(result.ParamsJson),
        result.TotalReturn,
        result.SortinoRatio,
        result.SharpeRatio,
        result.WinRate,
        result.TotalTrades,
        result.ProfitFactor,
        result.CalmarRatio,
        result.AnnualizedReturn,
        result.OosTotalReturn,
        result.OosSortinoRatio,
        result.OosSharpeRatio,
        result.OosWinRate,
        result.OosTotalTrades,
        result.OosProfitFactor,
        result.OosCalmarRatio,
        result.OosAnnualizedReturn);

    private static OptimizeParamSnapshot? DeserializeParameters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<OptimizeParamSnapshot>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

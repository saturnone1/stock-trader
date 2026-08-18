using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>최적화 작업 관리 포트를 SQLite 엔티티와 명시적으로 변환합니다.</summary>
public sealed class OptimizationJobManagementStore : IOptimizationJobManagementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public OptimizationJobManagementStore(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<OptimizationJobRecord> CreateAsync(
        OptimizationJobRecord job,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = ToEntity(job);
        db.OptimizationJobs.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ToRecord(entity, []);
    }

    public async Task<IReadOnlyList<OptimizationJobRecord>> ListAsync(
        OptimizationJobControlState? state,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OptimizationJobs.AsNoTracking();
        if (state.HasValue)
        {
            var storedState = OptimizationJobStateMapper.ToStorage(state.Value);
            query = query.Where(job => job.Status == storedState);
        }

        var jobs = await query
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);
        return jobs.Select(job => ToRecord(job, [])).ToList();
    }

    public async Task<OptimizationJobRecord?> FindAsync(
        int jobId,
        int topResults,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var job = topResults > 0
            ? await db.OptimizationJobs
                .AsNoTracking()
                .Include(item => item.Results
                    .OrderBy(result => result.Rank)
                    .Take(topResults))
                .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken)
            : await db.OptimizationJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null) return null;

        var results = topResults > 0
            ? job.Results.Select(ToResult).ToList()
            : [];
        return ToRecord(job, results);
    }

    public async Task<bool> UpdateSettingsAsync(
        int jobId,
        bool? autoApplyBestResult,
        int? autoApplyMinTrades,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OptimizationJobs.Where(job => job.Id == jobId);
        var affected = (autoApplyBestResult.HasValue, autoApplyMinTrades.HasValue) switch
        {
            (true, true) => await query.ExecuteUpdateAsync(
                update => update
                    .SetProperty(
                        job => job.AutoApplyBestResult,
                        autoApplyBestResult!.Value)
                    .SetProperty(
                        job => job.AutoApplyMinTrades,
                        autoApplyMinTrades!.Value),
                cancellationToken),
            (true, false) => await query.ExecuteUpdateAsync(
                update => update.SetProperty(
                    job => job.AutoApplyBestResult,
                    autoApplyBestResult!.Value),
                cancellationToken),
            (false, true) => await query.ExecuteUpdateAsync(
                update => update.SetProperty(
                    job => job.AutoApplyMinTrades,
                    autoApplyMinTrades!.Value),
                cancellationToken),
            _ => await query.CountAsync(cancellationToken)
        };
        return affected == 1;
    }

    public async Task<bool> TryDeleteAsync(
        int jobId,
        OptimizationJobControlState expectedState,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var storedState = OptimizationJobStateMapper.ToStorage(expectedState);
        var affected = await db.OptimizationJobs
            .Where(job => job.Id == jobId && job.Status == storedState)
            .ExecuteDeleteAsync(cancellationToken);
        return affected == 1;
    }

    private static OptimizationJob ToEntity(OptimizationJobRecord job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Status = OptimizationJobStateMapper.ToStorage(job.State),
        Priority = job.Priority,
        RequestJson = job.RequestJson,
        TotalCombinations = job.TotalCombinations,
        TestedCombinations = job.TestedCombinations,
        CurrentChunkIndex = job.CurrentChunkIndex,
        ChunkSize = job.ChunkSize,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        LastProgressAt = job.LastProgressAt,
        MaxDurationHours = job.MaxDurationHours,
        MaxTestedCombinations = job.MaxTestedCombinations,
        RankBy = job.RankBy,
        TopResultsToKeep = job.TopResultsToKeep,
        ContinuousMode = job.ContinuousMode,
        AutoApplyBestResult = job.AutoApplyBestResult,
        AutoApplyMinTrades = job.AutoApplyMinTrades,
        AppliedResultCount = job.AppliedResultCount,
        LastAutoAppliedAt = job.LastAutoAppliedAt,
        LastAutoAppliedResultId = job.LastAutoAppliedResultId,
        LastAutoApplyMessage = job.LastAutoApplyMessage,
        ErrorMessage = job.ErrorMessage
    };

    private static OptimizationJobRecord ToRecord(
        OptimizationJob job,
        IReadOnlyList<OptimizeResultItem> results) => new()
    {
        Id = job.Id,
        Name = job.Name,
        State = OptimizationJobStateMapper.ToApplication(job.Status),
        Priority = job.Priority,
        RequestJson = job.RequestJson,
        TotalCombinations = job.TotalCombinations,
        TestedCombinations = job.TestedCombinations,
        CurrentChunkIndex = job.CurrentChunkIndex,
        ChunkSize = job.ChunkSize,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        LastProgressAt = job.LastProgressAt,
        MaxDurationHours = job.MaxDurationHours,
        MaxTestedCombinations = job.MaxTestedCombinations,
        RankBy = job.RankBy,
        TopResultsToKeep = job.TopResultsToKeep,
        ContinuousMode = job.ContinuousMode,
        AutoApplyBestResult = job.AutoApplyBestResult,
        AutoApplyMinTrades = job.AutoApplyMinTrades,
        AppliedResultCount = job.AppliedResultCount,
        LastAutoAppliedAt = job.LastAutoAppliedAt,
        LastAutoAppliedResultId = job.LastAutoAppliedResultId,
        LastAutoApplyMessage = job.LastAutoApplyMessage,
        ErrorMessage = job.ErrorMessage,
        Results = results
    };

    private static OptimizeResultItem ToResult(OptimizationResult result)
    {
        OptimizeParamSnapshot? parameters = null;
        try
        {
            parameters = JsonSerializer.Deserialize<OptimizeParamSnapshot>(
                result.ParamsJson,
                JsonOptions);
        }
        catch (Exception)
        {
            // 오래된 손상 행은 빈 파라미터로 표시하던 기존 API 호환 동작을 유지합니다.
        }

        return new OptimizeResultItem
        {
            Id = result.Id,
            Rank = result.Rank,
            Params = parameters ?? new OptimizeParamSnapshot(),
            TotalReturn = result.TotalReturn,
            SortinoRatio = result.SortinoRatio,
            SharpeRatio = result.SharpeRatio,
            MaxDrawdown = result.MaxDrawdown,
            WinRate = result.WinRate,
            TotalTrades = result.TotalTrades,
            ProfitFactor = result.ProfitFactor,
            CalmarRatio = result.CalmarRatio,
            AnnualizedReturn = result.AnnualizedReturn,
            OosTotalReturn = result.OosTotalReturn,
            OosSortinoRatio = result.OosSortinoRatio,
            OosSharpeRatio = result.OosSharpeRatio,
            OosMaxDrawdown = result.OosMaxDrawdown,
            OosWinRate = result.OosWinRate,
            OosTotalTrades = result.OosTotalTrades,
            OosProfitFactor = result.OosProfitFactor,
            OosCalmarRatio = result.OosCalmarRatio,
            OosAnnualizedReturn = result.OosAnnualizedReturn
        };
    }
}

using StockTrader.Application.Optimization;

namespace StockTrader.Api;

internal static class OptimizationJobApiMapper
{
    public static OptimizeJobSummary ToSummary(OptimizationJobSummaryView job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Status = job.State.ToString(),
        TotalCombinations = job.TotalCombinations,
        TestedCombinations = job.TestedCombinations,
        ProgressPercent = job.ProgressPercent,
        CreatedAt = job.CreatedAt,
        StartedAt = job.StartedAt,
        ContinuousMode = job.ContinuousMode,
        AutoApplyBestResult = job.AutoApplyBestResult,
        AutoApplyMinTrades = job.AutoApplyMinTrades,
        AppliedResultCount = job.AppliedResultCount,
        LastAutoAppliedAt = job.LastAutoAppliedAt,
        LastAutoAppliedResultId = job.LastAutoAppliedResultId,
        LastAutoApplyMessage = job.LastAutoApplyMessage
    };

    public static OptimizeJobDetail ToDetail(OptimizationJobDetailView job) => new()
    {
        Id = job.Summary.Id,
        Name = job.Summary.Name,
        Status = job.Summary.State.ToString(),
        TotalCombinations = job.Summary.TotalCombinations,
        TestedCombinations = job.Summary.TestedCombinations,
        ProgressPercent = job.Summary.ProgressPercent,
        ElapsedSeconds = job.ElapsedSeconds,
        EstimatedRemainingSeconds = job.EstimatedRemainingSeconds,
        CreatedAt = job.Summary.CreatedAt,
        StartedAt = job.Summary.StartedAt,
        CompletedAt = job.CompletedAt,
        LastProgressAt = job.LastProgressAt,
        ErrorMessage = job.ErrorMessage,
        ContinuousMode = job.Summary.ContinuousMode,
        AutoApplyBestResult = job.Summary.AutoApplyBestResult,
        AutoApplyMinTrades = job.Summary.AutoApplyMinTrades,
        AppliedResultCount = job.Summary.AppliedResultCount,
        LastAutoAppliedAt = job.Summary.LastAutoAppliedAt,
        LastAutoAppliedResultId = job.Summary.LastAutoAppliedResultId,
        LastAutoApplyMessage = job.Summary.LastAutoApplyMessage,
        TopResults = job.TopResults.Count > 0 ? job.TopResults.ToList() : null
    };
}

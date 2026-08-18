using Microsoft.EntityFrameworkCore;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

internal static class OptimizationResultPersistence
{
    public static async Task MergeRankedAsync(
        AppDbContext db,
        int jobId,
        IReadOnlyList<OptimizationResult> newResults,
        int topResultsToKeep,
        string rankBy)
    {
        var existing = await db.OptimizationResults
            .Where(result => result.JobId == jobId)
            .OrderBy(result => result.Rank)
            .Take(topResultsToKeep)
            .ToListAsync();
        var merged = Sort(existing.Concat(newResults).ToList(), rankBy);
        var toKeep = merged.Take(topResultsToKeep).ToList();
        var existingIds = existing.Select(result => result.Id).ToHashSet();
        var removeIds = merged.Skip(topResultsToKeep)
            .Where(result => result.Id != 0 && existingIds.Contains(result.Id))
            .Select(result => result.Id)
            .ToHashSet();

        db.OptimizationResults.RemoveRange(
            existing.Where(result => removeIds.Contains(result.Id)));
        for (var index = 0; index < toKeep.Count; index++)
        {
            var result = toKeep[index];
            result.JobId = jobId;
            result.Rank = index + 1;
            if (result.Id == 0) db.OptimizationResults.Add(result);
        }
    }

    private static List<OptimizationResult> Sort(
        List<OptimizationResult> results, string rankBy) => rankBy.ToLowerInvariant() switch
        {
            "totalreturn" => results.OrderByDescending(result => result.TotalReturn).ToList(),
            "sortinoratio" => results.OrderByDescending(result => result.SortinoRatio).ToList(),
            "sharperatio" => results.OrderByDescending(result => result.SharpeRatio).ToList(),
            "calmarratio" => results.OrderByDescending(result => result.CalmarRatio).ToList(),
            "profitfactor" => results.OrderByDescending(result => result.ProfitFactor).ToList(),
            "winrate" => results.OrderByDescending(result => result.WinRate).ToList(),
            "annualizedreturn" => results.OrderByDescending(result => result.AnnualizedReturn).ToList(),
            _ => results.OrderByDescending(result => result.SortinoRatio).ToList()
        };
}

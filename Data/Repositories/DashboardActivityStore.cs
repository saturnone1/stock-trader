using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Dashboard;
using StockTrader.Domain.Trading;

namespace StockTrader.Data.Repositories;

public sealed class DashboardActivityStore(
    IDbContextFactory<AppDbContext> dbFactory) : IDashboardActivityStore
{
    public async Task<DashboardActivitySnapshot> GetAsync(
        int recommendationCount,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activeSignalCount = await db.PatternSignals
            .AsNoTracking()
            .CountAsync(signal => signal.IsActive && !signal.IsSuperseded, ct);
        var rows = await db.TradeRecommendations
            .AsNoTracking()
            .Where(recommendation => !recommendation.IsSuperseded)
            .OrderByDescending(recommendation => recommendation.GeneratedAt)
            .ThenByDescending(recommendation => recommendation.Id)
            .Take(Math.Max(0, recommendationCount))
            .Select(recommendation => new RecommendationRow(
                recommendation.Id,
                recommendation.Symbol,
                recommendation.PatternType,
                recommendation.EntryPrice,
                recommendation.StopLossPrice,
                recommendation.TargetPrice,
                recommendation.Expectancy,
                recommendation.WasExecuted,
                recommendation.GeneratedAt))
            .ToArrayAsync(ct);
        var recommendations = rows.Select(row => new DashboardRecommendationSnapshot(
            row.Id,
            row.Symbol,
            row.PatternType.ToString(),
            row.EntryPrice,
            row.StopLossPrice,
            row.TargetPrice,
            RiskRewardRatioPolicy.CalculateWithAbsoluteStopDistance(
                row.EntryPrice,
                row.StopLossPrice,
                row.TargetPrice),
            row.Expectancy,
            row.WasExecuted,
            row.GeneratedAt)).ToArray();

        return new DashboardActivitySnapshot(activeSignalCount, recommendations);
    }

    private sealed record RecommendationRow(
        long Id,
        string Symbol,
        PatternType PatternType,
        decimal EntryPrice,
        decimal StopLossPrice,
        decimal TargetPrice,
        decimal Expectancy,
        bool WasExecuted,
        DateTime GeneratedAt);
}

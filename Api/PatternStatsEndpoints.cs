using StockTrader.Data.Repositories;

namespace StockTrader.Api;

public static class PatternStatsEndpoints
{
    public static RouteGroupBuilder MapPatternStatsApi(this RouteGroupBuilder group)
    {
        // GET /api/pattern-stats
        group.MapGet("/pattern-stats", async (
            IPatternStatsRepository statsRepo,
            CancellationToken ct) =>
        {
            var stats = await statsRepo.GetAllAsync(ct);

            return Results.Ok(new
            {
                Count = stats.Count,
                Stats = stats
                    .OrderByDescending(s => s.Expectancy)
                    .Select(s => new
                    {
                        Pattern             = s.PatternType.ToString(),
                        s.Symbol,
                        s.SampleSize,
                        s.WinRate,
                        s.AvgWinPercent,
                        s.AvgLossPercent,
                        s.MaxDrawdownPercent,
                        s.Expectancy,
                        s.ProfitFactor,
                        LastUpdated         = s.LastUpdated.ToString("o")
                    })
            });
        }).RequireAuthorization();

        return group;
    }
}

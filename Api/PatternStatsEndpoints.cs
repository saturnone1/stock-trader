using StockTrader.Api.Contracts;
using StockTrader.Application.Statistics;

namespace StockTrader.Api;

public static class PatternStatsEndpoints
{
    public static RouteGroupBuilder MapPatternStatsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/pattern-stats", async (
            IPatternStatisticsQuery query,
            CancellationToken ct) =>
        {
            var statistics = await query.GetByExpectancyAsync(ct);
            return Results.Ok(new PatternStatisticsListResponse(
                statistics.Count,
                statistics.Select(PatternStatisticsResponseMapper.Map).ToArray()));
        })
            .Produces<PatternStatisticsListResponse>()
            .RequireAuthorization();
        return group;
    }
}

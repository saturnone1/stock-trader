using StockTrader.Api.Contracts;
using StockTrader.Application.Risk;

namespace StockTrader.Api;

public static class RiskEndpoints
{
    public static RouteGroupBuilder MapRiskApi(this RouteGroupBuilder group)
    {
        group.MapGet("/risk", async (IRiskOverviewQuery query, CancellationToken ct) =>
                Results.Ok(RiskOverviewResponse.Create(await query.GetAsync(ct))))
            .Produces<RiskOverviewResponse>()
            .RequireAuthorization();
        return group;
    }
}

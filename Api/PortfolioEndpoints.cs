using StockTrader.Api.Contracts;
using StockTrader.Application.Portfolio;

namespace StockTrader.Api;

public static class PortfolioEndpoints
{
    public static RouteGroupBuilder MapPortfolioApi(this RouteGroupBuilder group)
    {
        group.MapGet("/portfolio", async (
            IOpenPositionQuery query,
            CancellationToken ct) =>
        {
            var snapshot = await query.GetAsync(ct);
            return Results.Ok(new PortfolioHoldingsResponse(
                snapshot.Positions.Select(OpenPositionResponseMapper.Map).ToArray(),
                snapshot.TotalUnrealizedPnL,
                snapshot.Count));
        })
            .Produces<PortfolioHoldingsResponse>()
            .RequireAuthorization();

        group.MapGet("/portfolio/performance", async (
            IPortfolioPerformanceQuery query,
            CancellationToken ct) =>
                Results.Ok(PortfolioPerformanceResponse.Create(await query.GetAsync(ct))))
            .Produces<PortfolioPerformanceResponse>()
            .RequireAuthorization();

        return group;
    }
}

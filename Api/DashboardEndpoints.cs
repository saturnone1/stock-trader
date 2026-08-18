using StockTrader.Api.Contracts;
using StockTrader.Application.Dashboard;

namespace StockTrader.Api;

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardApi(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", async (
                IDashboardQuery query,
                CancellationToken ct) =>
                Results.Ok(DashboardResponse.Create(await query.GetAsync(ct))))
            .Produces<DashboardResponse>()
            .RequireAuthorization();

        return group;
    }
}

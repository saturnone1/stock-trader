using StockTrader.Api.Contracts;
using StockTrader.Application.Signals;

namespace StockTrader.Api;

public static class SignalEndpoints
{
    public static RouteGroupBuilder MapSignalApi(this RouteGroupBuilder group)
    {
        group.MapGet("/signals", async (
            ISignalListQuery query,
            string? pattern,
            string? search,
            string? sort,
            string? style,
            CancellationToken ct) =>
                Results.Ok(SignalListResponse.Create(await query.GetAsync(
                    new SignalBrowseRequest(pattern, search, sort, style),
                    ct))))
            .Produces<SignalListResponse>()
            .RequireAuthorization();
        return group;
    }
}

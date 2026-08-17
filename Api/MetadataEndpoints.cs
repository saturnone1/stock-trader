using StockTrader.Api.Contracts;

namespace StockTrader.Api;

public static class MetadataEndpoints
{
    public static RouteGroupBuilder MapMetadataApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/metadata").RequireAuthorization();

        group.MapGet("/strategy-builder", () => Results.Ok(StrategyBuilderMetadataResponse.Create()));

        return api;
    }
}

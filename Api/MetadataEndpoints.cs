using StockTrader.Api.Contracts;
using StockTrader.Configuration;
using Microsoft.Extensions.Options;

namespace StockTrader.Api;

public static class MetadataEndpoints
{
    public static RouteGroupBuilder MapMetadataApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/metadata").RequireAuthorization();

        group.MapGet("/strategy-builder", (
            IOptions<OptimizationWorkerTransportOptions> optimization,
            IOptions<MarketDataTransportOptions> marketData) =>
            Results.Ok(StrategyBuilderMetadataResponse.Create(optimization.Value, marketData.Value)))
            .Produces<StrategyBuilderMetadataResponse>();

        return api;
    }
}

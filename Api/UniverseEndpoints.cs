using StockTrader.Api.Contracts;
using StockTrader.Application.Research;

namespace StockTrader.Api;

public static class UniverseEndpoints
{
    public static RouteGroupBuilder MapUniverseApi(this RouteGroupBuilder group)
    {
        group.MapGet("/universe/meta", async (
            ResearchUniverseQueryService service,
            CancellationToken ct) =>
                Results.Ok(ResearchUniverseMetaResponse.Create(
                    await service.GetMetaAsync(ct))))
            .Produces<ResearchUniverseMetaResponse>()
            .RequireAuthorization();

        group.MapGet("/universe/query", async (
            string? search,
            string? sectors,
            string? industries,
            decimal? marketCapMin,
            decimal? marketCapMax,
            decimal? percentileMin,
            decimal? percentileMax,
            int? limit,
            string? sortBy,
            ResearchUniverseQueryService service,
            CancellationToken ct) =>
        {
            var result = await service.QueryAsync(new ResearchUniverseQuery(
                search,
                sectors,
                industries,
                marketCapMin,
                marketCapMax,
                percentileMin,
                percentileMax,
                limit,
                sortBy), ct);
            return Results.Ok(ResearchUniverseQueryResponse.Create(result));
        })
            .Produces<ResearchUniverseQueryResponse>()
            .RequireAuthorization();

        return group;
    }
}

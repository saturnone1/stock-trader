using StockTrader.Api.Contracts;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Trading;

namespace StockTrader.Api;

public static class TradeEndpoints
{
    public static RouteGroupBuilder MapTradeApi(this RouteGroupBuilder group)
    {
        group.MapGet("/trades/recommendations", GetRecommendationsAsync)
            .Produces<TradeRecommendationListResponse>()
            .Produces<TradeActivityErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapGet("/trades/positions", async (
            IOpenPositionQuery query,
            CancellationToken ct) =>
        {
            var snapshot = await query.GetAsync(ct);
            return Results.Ok(new OpenPositionsResponse(
                snapshot.Count,
                snapshot.Positions.Select(OpenPositionResponseMapper.Map).ToArray()));
        })
            .Produces<OpenPositionsResponse>()
            .RequireAuthorization();

        group.MapGet("/trades/history", GetHistoryAsync)
            .Produces<TradeHistoryResponse>()
            .Produces<TradeActivityErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetRecommendationsAsync(
        ITradeActivityQuery query,
        int? count,
        CancellationToken ct)
    {
        var outcome = await query.GetRecommendationsAsync(count, ct);
        return outcome.Succeeded
            ? Results.Ok(TradeRecommendationListResponse.Create(outcome.Value!))
            : Results.BadRequest(new TradeActivityErrorResponse(outcome.Errors));
    }

    private static async Task<IResult> GetHistoryAsync(
        ITradeActivityQuery query,
        PatternType? pattern,
        DateTime? from,
        DateTime? to,
        int? skip,
        int? take,
        CancellationToken ct)
    {
        var outcome = await query.GetHistoryAsync(
            new(pattern, from, to, skip, take), ct);
        return outcome.Succeeded
            ? Results.Ok(TradeHistoryResponse.Create(outcome.Value!))
            : Results.BadRequest(new TradeActivityErrorResponse(outcome.Errors));
    }
}

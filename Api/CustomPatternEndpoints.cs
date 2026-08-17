using StockTrader.Api.Contracts;
using StockTrader.Application.Strategies;

namespace StockTrader.Api;

public static class CustomPatternEndpoints
{
    public static RouteGroupBuilder MapCustomPatternApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/custom-patterns").RequireAuthorization();

        group.MapGet("/", async (CustomPatternManagementService service, CancellationToken ct) =>
            (await service.ListAsync(ct)).Select(value => value.ToResponse()).ToArray())
            .Produces<CustomPatternResponse[]>();

        group.MapGet("/{id:int}", async (int id, CustomPatternManagementService service, CancellationToken ct) =>
        {
            var pattern = await service.FindAsync(id, ct);
            return pattern is null ? Results.NotFound() : Results.Ok(pattern.ToResponse());
        })
            .Produces<CustomPatternResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CustomPatternWriteRequest request, CustomPatternManagementService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request.ToDefinition(), ct);
            return result.Kind == CustomPatternOperationKind.Success
                ? Results.Created($"/api/custom-patterns/{result.Definition!.Id}", result.Definition.ToResponse())
                : ToErrorResult(result);
        })
            .Produces<CustomPatternResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/{id:int}", async (int id, CustomPatternWriteRequest request, CustomPatternManagementService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, request.ToDefinition(), ct);
            return result.Kind == CustomPatternOperationKind.Success
                ? Results.Ok(result.Definition!.ToResponse())
                : ToErrorResult(result);
        })
            .Produces<CustomPatternResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}", async (int id, CustomPatternManagementService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.Ok() : Results.NotFound());

        group.MapPost("/{id:int}/apply-backtest", async (int id, BacktestApplyRequest request, CustomPatternManagementService service, CancellationToken ct) =>
        {
            var result = await service.ApplyBacktestAsync(id, new BacktestStrategyParameterUpdate(
                request.AtrStopMultiplier,
                request.AtrTargetMultiplier,
                request.MaxHoldingBars,
                request.TrailingAtr,
                request.PartialProfitR), ct);
            return result.Kind == CustomPatternOperationKind.Success
                ? Results.Ok(result.Definition!.ToResponse())
                : ToErrorResult(result);
        })
            .Produces<CustomPatternResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return api;
    }

    private static IResult ToErrorResult(CustomPatternOperationResult result) => result.Kind switch
    {
        CustomPatternOperationKind.Invalid => Results.BadRequest(
            new { error = result.Error, errors = result.Errors }),
        CustomPatternOperationKind.Conflict => Results.Conflict(new { error = result.Error }),
        _ => Results.NotFound(new { error = result.Error })
    };
}

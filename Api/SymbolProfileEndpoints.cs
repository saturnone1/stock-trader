using StockTrader.Api.Contracts;
using StockTrader.Application.SymbolProfiles;

namespace StockTrader.Api;

public static class SymbolProfileEndpoints
{
    public static RouteGroupBuilder MapSymbolProfileApi(this RouteGroupBuilder group)
    {
        group.MapGet("/symbol-profiles", async (
            SymbolProfileManagementService service,
            CancellationToken ct) =>
                Results.Ok((await service.ListAsync(ct: ct)).Select(SymbolProfileResponse.Create)))
            .Produces<IReadOnlyList<SymbolProfileResponse>>()
            .RequireAuthorization();

        group.MapGet("/symbol-profiles/{symbol}", async (
            string symbol,
            SymbolProfileManagementService service,
            CancellationToken ct) =>
                Results.Ok((await service.ListAsync(symbol, ct)).Select(SymbolProfileResponse.Create)))
            .Produces<IReadOnlyList<SymbolProfileResponse>>()
            .RequireAuthorization();

        group.MapPost("/symbol-profiles", async (
            SymbolProfileUpsertRequest request,
            SymbolProfileManagementService service,
            CancellationToken ct) =>
        {
            var outcome = await service.UpsertAsync(request.ToCommand(), ct);
            if (!outcome.Succeeded)
                return Results.BadRequest(new SymbolProfileErrorResponse(outcome.Errors));

            var response = SymbolProfileResponse.Create(outcome.Profile!);
            return outcome.Created
                ? Results.Created($"/api/symbol-profiles/{response.Id}", response)
                : Results.Ok(response);
        })
            .Accepts<SymbolProfileUpsertRequest>("application/json")
            .Produces<SymbolProfileResponse>()
            .Produces<SymbolProfileResponse>(StatusCodes.Status201Created)
            .Produces<SymbolProfileErrorResponse>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapPost("/symbol-profiles/{id:long}/activate", async (
            long id,
            SymbolProfileManagementService service,
            CancellationToken ct) =>
        {
            var profile = await service.ActivateAsync(id, ct);
            return profile is null
                ? Results.NotFound()
                : Results.Ok(new SymbolProfileActionResponse(
                    $"{profile.Symbol} - {profile.Name} 프로파일이 활성화되었습니다."));
        })
            .Produces<SymbolProfileActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapPost("/symbol-profiles/{id:long}/deactivate", async (
            long id,
            SymbolProfileManagementService service,
            CancellationToken ct) =>
        {
            var profile = await service.DeactivateAsync(id, ct);
            return profile is null
                ? Results.NotFound()
                : Results.Ok(new SymbolProfileActionResponse(
                    $"{profile.Symbol} - {profile.Name} 프로파일이 비활성화되었습니다."));
        })
            .Produces<SymbolProfileActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapDelete("/symbol-profiles/{id:long}", async (
            long id,
            SymbolProfileManagementService service,
            CancellationToken ct) =>
                await service.DeleteAsync(id, ct)
                    ? Results.Ok(new SymbolProfileActionResponse("삭제되었습니다."))
                    : Results.NotFound())
            .Produces<SymbolProfileActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return group;
    }
}

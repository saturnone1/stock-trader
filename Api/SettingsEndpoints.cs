using StockTrader.Api.Contracts;
using StockTrader.Application.Settings;

namespace StockTrader.Api;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (
            SettingsManagementService service,
            CancellationToken ct) =>
        {
            var settings = await service.GetAsync(ct);
            return Results.Ok(SettingsResponse.Create(settings));
        })
        .Produces<SettingsResponse>()
        .RequireAuthorization();

        group.MapPut("/settings", async (
            SettingsUpdateRequest request,
            SettingsManagementService service,
            CancellationToken ct) =>
        {
            var outcome = await service.UpdateAsync(request.ToCommand(), ct);
            if (!outcome.Succeeded)
                return Results.BadRequest(new SettingsErrorResponse(outcome.Errors));

            return Results.Ok(new SettingsUpdateResponse(
                "설정이 저장되었습니다.",
                outcome.Settings!.LastModified));
        })
        .Accepts<SettingsUpdateRequest>("application/json")
        .Produces<SettingsUpdateResponse>()
        .Produces<SettingsErrorResponse>(StatusCodes.Status400BadRequest)
        .RequireAuthorization();

        return group;
    }
}

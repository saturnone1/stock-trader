using StockTrader.Api.Contracts;
using StockTrader.Application.Accounts;
using StockTrader.Services.Account;

namespace StockTrader.Api;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountApi(this RouteGroupBuilder group)
    {
        group.MapGet("/accounts/meta", () =>
            Results.Ok(TradingAccountMetadataResponse.Create()))
            .Produces<TradingAccountMetadataResponse>()
            .RequireAuthorization();

        group.MapGet("/accounts", async (
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var accounts = await accountManager.GetAllAccountsAsync(ct);
            return Results.Ok(new TradingAccountListResponse(
                accounts.Count,
                accounts.Select(TradingAccountResponse.Create).ToArray()));
        })
        .Produces<TradingAccountListResponse>()
        .RequireAuthorization();

        group.MapPost("/accounts", async (
            TradingAccountCreateRequest request,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var account = request.ToManaged();
            var validation = TradingAccountPolicy.Validate(account);
            if (!validation.Succeeded)
                return Results.BadRequest(new TradingAccountErrorResponse(validation.Errors));

            var created = await accountManager.AddAccountAsync(account, ct);
            return Results.Created(
                $"/api/accounts/{created.Id}",
                TradingAccountResponse.Create(created));
        })
        .Accepts<TradingAccountCreateRequest>("application/json")
        .Produces<TradingAccountResponse>(StatusCodes.Status201Created)
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status400BadRequest)
        .RequireAuthorization();

        group.MapPut("/accounts/{id:int}", async (
            int id,
            TradingAccountUpdateRequest request,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing is null)
            {
                return Results.NotFound(new TradingAccountErrorResponse(
                    [$"Account {id} not found."]));
            }

            var account = request.ApplyTo(existing);
            var validation = TradingAccountPolicy.Validate(account);
            if (!validation.Succeeded)
                return Results.BadRequest(new TradingAccountErrorResponse(validation.Errors));

            var updated = await accountManager.UpdateAccountAsync(account, ct);
            return Results.Ok(TradingAccountResponse.Create(updated));
        })
        .Accepts<TradingAccountUpdateRequest>("application/json")
        .Produces<TradingAccountResponse>()
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status404NotFound)
        .RequireAuthorization();

        group.MapDelete("/accounts/{id:int}", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            if (await accountManager.GetAccountByIdAsync(id, ct) is null)
            {
                return Results.NotFound(new TradingAccountErrorResponse(
                    [$"Account {id} not found."]));
            }

            await accountManager.DeleteAccountAsync(id, ct);
            return Results.Ok(new TradingAccountMessageResponse(
                $"Account {id} deleted."));
        })
        .Produces<TradingAccountMessageResponse>()
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status404NotFound)
        .RequireAuthorization();

        group.MapPost("/accounts/{id:int}/test", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            if (await accountManager.GetAccountByIdAsync(id, ct) is null)
            {
                return Results.NotFound(new TradingAccountErrorResponse(
                    [$"Account {id} not found."]));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var status = await accountManager.GetConnectionStatusAsync(id, timeout.Token);
            return Results.Ok(AccountConnectionStatusResponse.Create(status));
        })
        .Produces<AccountConnectionStatusResponse>()
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status404NotFound)
        .RequireAuthorization();

        group.MapPost("/accounts/{id:int}/activate", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing is null)
            {
                return Results.NotFound(new TradingAccountErrorResponse(
                    [$"Account {id} not found."]));
            }
            if (!existing.IsEnabled)
            {
                return Results.BadRequest(new TradingAccountErrorResponse(
                    ["A disabled account cannot be activated."]));
            }

            await accountManager.SetActiveAccountAsync(id, ct);
            return Results.Ok(new TradingAccountMessageResponse(
                $"Account {id} is now active."));
        })
        .Produces<TradingAccountMessageResponse>()
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<TradingAccountErrorResponse>(StatusCodes.Status404NotFound)
        .RequireAuthorization();

        return group;
    }
}

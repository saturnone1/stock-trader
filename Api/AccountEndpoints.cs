using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;

namespace StockTrader.Api;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountApi(this RouteGroupBuilder group)
    {
        // GET /api/accounts
        group.MapGet("/accounts", async (
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var accounts = await accountManager.GetAllAccountsAsync(ct);

            return Results.Ok(new
            {
                Count = accounts.Count,
                Accounts = accounts.Select(a => new
                {
                    a.Id,
                    a.AccountName,
                    BrokerType      = a.BrokerType.ToString(),
                    ApiKey          = MaskKey(a.ApiKey),
                    a.Environment,
                    a.IsActive,
                    a.IsEnabled,
                    a.Notes,
                    CreatedAt       = a.CreatedAt.ToString("o"),
                    UpdatedAt       = a.UpdatedAt.ToString("o"),
                    LastConnectedAt = a.LastConnectedAt?.ToString("o")
                })
            });
        }).RequireAuthorization();

        // POST /api/accounts
        group.MapPost("/accounts", async (
            HttpContext ctx,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            TradingAccount? account;
            try
            {
                account = await ctx.Request.ReadFromJsonAsync<TradingAccount>(ct);
                if (account == null)
                    return Results.BadRequest(new { error = "Invalid JSON body." });
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body for TradingAccount." });
            }

            if (string.IsNullOrWhiteSpace(account.AccountName))
                return Results.BadRequest(new { error = "AccountName is required." });

            if (!Enum.IsDefined(typeof(BrokerType), account.BrokerType))
                return Results.BadRequest(new { error = "Invalid BrokerType value." });

            account.CreatedAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;

            var created = await accountManager.AddAccountAsync(account, ct);

            return Results.Created($"/api/accounts/{created.Id}", new
            {
                created.Id,
                created.AccountName,
                BrokerType = created.BrokerType.ToString(),
                created.Environment,
                created.IsActive,
                created.IsEnabled,
                created.Notes,
                CreatedAt  = created.CreatedAt.ToString("o")
            });
        }).RequireAuthorization();

        // PUT /api/accounts/{id}
        group.MapPut("/accounts/{id:int}", async (
            int id,
            HttpContext ctx,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing == null)
                return Results.NotFound(new { error = $"Account {id} not found." });

            TradingAccount? updateRequest;
            try
            {
                updateRequest = await ctx.Request.ReadFromJsonAsync<TradingAccount>(ct);
                if (updateRequest == null)
                    return Results.BadRequest(new { error = "Invalid JSON body." });
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body for TradingAccount." });
            }

            // 기존 ID/생성일 보존
            updateRequest.Id        = id;
            updateRequest.CreatedAt = existing.CreatedAt;
            updateRequest.UpdatedAt = DateTime.UtcNow;

            var updated = await accountManager.UpdateAccountAsync(updateRequest, ct);

            return Results.Ok(new
            {
                updated.Id,
                updated.AccountName,
                BrokerType      = updated.BrokerType.ToString(),
                updated.Environment,
                updated.IsActive,
                updated.IsEnabled,
                updated.Notes,
                UpdatedAt       = updated.UpdatedAt.ToString("o"),
                LastConnectedAt = updated.LastConnectedAt?.ToString("o")
            });
        }).RequireAuthorization();

        // DELETE /api/accounts/{id}
        group.MapDelete("/accounts/{id:int}", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing == null)
                return Results.NotFound(new { error = $"Account {id} not found." });

            await accountManager.DeleteAccountAsync(id, ct);

            return Results.Ok(new { message = $"Account {id} deleted." });
        }).RequireAuthorization();

        // POST /api/accounts/{id}/test — 연결 테스트
        group.MapPost("/accounts/{id:int}/test", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing == null)
                return Results.NotFound(new { error = $"Account {id} not found." });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var status = await accountManager.GetConnectionStatusAsync(id, cts.Token);

            return Results.Ok(new
            {
                status.AccountId,
                status.IsConnected,
                status.StatusMessage,
                status.TotalEquity,
                status.Cash,
                status.BuyingPower,
                status.OpenPositionCount,
                CheckedAt = status.CheckedAt.ToString("o")
            });
        }).RequireAuthorization();

        // POST /api/accounts/{id}/activate — 활성 계좌 전환
        group.MapPost("/accounts/{id:int}/activate", async (
            int id,
            IAccountManager accountManager,
            CancellationToken ct) =>
        {
            var existing = await accountManager.GetAccountByIdAsync(id, ct);
            if (existing == null)
                return Results.NotFound(new { error = $"Account {id} not found." });

            await accountManager.SetActiveAccountAsync(id, ct);

            return Results.Ok(new { message = $"Account {id} is now active." });
        }).RequireAuthorization();

        return group;
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        if (key.Length <= 4) return "****";
        return key[..4] + new string('*', Math.Min(key.Length - 4, 8));
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Migrations;

namespace StockTrader.Api;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", async (
            IOptions<AlpacaSettings> alpaca,
            TimeProvider clock,
            DatabaseMigrationStatusProvider migrations,
            CancellationToken ct) =>
        {
            var databaseMigration = await migrations.GetAsync(ct);
            return Results.Ok(new
            {
                status = "ok",
                service = "stocktrader-api",
                alpacaConfigured = alpaca.Value.HasConfiguredCredentials,
                databaseMigration,
                timestamp = clock.GetUtcNow(),
            });
        });
        return api;
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Migrations;
using StockTrader.Application.Optimization;

namespace StockTrader.Api;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", async (
            IOptions<AlpacaSettings> alpaca,
            TimeProvider clock,
            DatabaseMigrationStatusProvider migrations,
            IOptimizationShadowResultCoordinator comparisons,
            IOptimizationWorkerLeaseMonitor workerLeases,
            CancellationToken ct) =>
        {
            var databaseMigration = await migrations.GetAsync(ct);
            var shadowComparisons = await comparisons.GetSummaryAsync(ct);
            var optimizationWorker = await workerLeases.GetOperationalSummaryAsync(
                clock.GetUtcNow().UtcDateTime, ct);
            return Results.Ok(new
            {
                status = "ok",
                service = "stocktrader-api",
                alpacaConfigured = alpaca.Value.HasConfiguredCredentials,
                databaseMigration,
                shadowComparisons,
                optimizationWorker,
                timestamp = clock.GetUtcNow(),
            });
        });
        return api;
    }
}

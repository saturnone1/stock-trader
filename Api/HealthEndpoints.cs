using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Migrations;
using StockTrader.Application.Optimization;
using StockTrader.Services.DataFeed;

namespace StockTrader.Api;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder api)
    {
        // Liveness describes only this process. A downstream outage should remove the
        // API from service through readiness, not restart it and amplify the failure.
        api.MapGet("/health/live", () => Results.Ok(new
        {
            status = "ok",
            service = "stocktrader-api",
        }));

        api.MapGet("/health", async (
            IOptions<AlpacaSettings> alpaca,
            TimeProvider clock,
            DatabaseMigrationStatusProvider migrations,
            IOptimizationShadowResultCoordinator comparisons,
            IOptimizationWorkerLeaseMonitor workerLeases,
            IOptions<MarketDataTransportOptions> marketDataTransport,
            MarketDataServiceClient marketDataClient,
            CancellationToken ct) =>
        {
            var databaseMigration = await migrations.GetAsync(ct);
            var shadowComparisons = await comparisons.GetSummaryAsync(ct);
            var optimizationWorker = await workerLeases.GetOperationalSummaryAsync(
                clock.GetUtcNow().UtcDateTime, ct);
            object? marketData = null;
            Exception? marketDataError = null;
            if (marketDataTransport.Value.Mode != MarketDataTransportMode.Local)
            {
                try { marketData = await marketDataClient.StatusAsync(ct); }
                catch (Exception error) { marketDataError = error; }
            }
            var healthy = marketDataTransport.Value.Mode != MarketDataTransportMode.Remote
                          || marketDataError is null;
            return Results.Json(new
            {
                status = healthy ? "ok" : "degraded",
                service = "stocktrader-api",
                alpacaConfigured = alpaca.Value.HasConfiguredCredentials,
                databaseMigration,
                shadowComparisons,
                optimizationWorker,
                marketDataMode = marketDataTransport.Value.Mode.ToString(),
                marketData,
                marketDataError = marketDataError?.GetType().Name,
                timestamp = clock.GetUtcNow(),
            }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
        return api;
    }
}

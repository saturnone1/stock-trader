using Microsoft.Extensions.Options;
using StockTrader.Configuration;

namespace StockTrader.Api;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthApi(this RouteGroupBuilder api)
    {
        api.MapGet("/health", (IOptions<AlpacaSettings> alpaca, TimeProvider clock) =>
            Results.Ok(new
            {
                status = "ok",
                service = "stocktrader-api",
                alpacaConfigured = alpaca.Value.HasConfiguredCredentials,
                timestamp = clock.GetUtcNow(),
            }));
        return api;
    }
}

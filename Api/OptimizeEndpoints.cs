using StockTrader.Application.Optimization;
using StockTrader.Services.Backtest;

namespace StockTrader.Api;

// ── 엔드포인트 ────────────────────────────────────────────────────────────────

public static class OptimizeEndpoints
{
    public static RouteGroupBuilder MapOptimizeApi(this RouteGroupBuilder api)
    {
        api.MapPost("/backtest/optimize", async (OptimizeRequest req, IBacktestService svc, CancellationToken ct) =>
        {
            var response = await svc.RunOptimizationAsync(req, ct);
            return Results.Ok(response);
        }).RequireAuthorization();

        return api;
    }
}

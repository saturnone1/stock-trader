using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Api;

public static class OptimizationWorkerEndpoints
{
    public static IEndpointRouteBuilder MapOptimizationWorkerApi(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/internal/optimization-worker")
            .RequireAuthorization(OptimizationWorkerAuthenticationDefaults.Policy)
            .RequireRateLimiting("optimization-worker")
            .ExcludeFromDescription();

        group.MapGet("/status", (
            ClaimsPrincipal worker,
            IOptions<OptimizationWorkerTransportOptions> transport) => Results.Ok(new
        {
            service = "strategy-research",
            workerId = worker.FindFirstValue(ClaimTypes.NameIdentifier),
            mode = transport.Value.Mode.ToString(),
            contractVersion = OptimizationWorkerContractCatalog.LeaseVersion,
            engineSemanticsVersion = OptimizationWorkerContractCatalog.EngineSemanticsVersion
        }));

        return api;
    }
}

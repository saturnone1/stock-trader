using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using StockTrader.Application.Optimization;
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

        group.MapGet("/status", async (
            ClaimsPrincipal worker,
            IOptions<OptimizationWorkerTransportOptions> transport,
            IOptimizationShadowResultCoordinator comparisons,
            CancellationToken ct) => Results.Ok(new
        {
            service = "strategy-research",
            workerId = worker.FindFirstValue(ClaimTypes.NameIdentifier),
            mode = transport.Value.Mode.ToString(),
            leaseTransportEnabled = transport.Value.LeaseTransportEnabled,
            contractVersion = OptimizationWorkerContractCatalog.LeaseVersion,
            engineSemanticsVersion = OptimizationWorkerContractCatalog.EngineSemanticsVersion,
            shadowComparisons = await comparisons.GetSummaryAsync(ct)
        }));

        group.MapPost("/leases/claim", async (
            ClaimsPrincipal worker,
            IOptimizationWorkerLeaseCoordinator leases,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var lease = await leases.TryLeaseAsync(WorkerId(worker), UtcNow(clock), ct);
            return lease is null ? Results.NoContent() : Results.Ok(lease);
        });

        group.MapPost("/leases/heartbeat", async (
            ClaimsPrincipal worker,
            OptimizationWorkerHeartbeat heartbeat,
            IOptimizationWorkerLeaseCoordinator leases,
            TimeProvider clock,
            CancellationToken ct) => Results.Ok(await leases.HeartbeatAsync(
                WorkerId(worker), heartbeat, UtcNow(clock), ct)));

        group.MapPost("/leases/result", async (
            ClaimsPrincipal worker,
            OptimizationWorkerResultSubmission submission,
            IOptimizationWorkerLeaseCoordinator leases,
            TimeProvider clock,
            CancellationToken ct) => Results.Ok(await leases.SubmitResultAsync(
                WorkerId(worker), submission, UtcNow(clock), ct)));

        return api;
    }

    private static string WorkerId(ClaimsPrincipal worker) =>
        worker.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private static DateTime UtcNow(TimeProvider clock) =>
        clock.GetUtcNow().UtcDateTime;
}

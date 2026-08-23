namespace StockTrader.Application.Optimization;

/// <summary>Keeps a shadow transport failure from changing the in-process optimization outcome.</summary>
public sealed class OptimizationShadowLeasePublisher(
    IOptimizationWorkerLeaseCoordinator leases,
    TimeProvider clock,
    ILogger<OptimizationShadowLeasePublisher> logger)
{
    public async Task PublishAsync(
        int jobId,
        OptimizationEvaluationContext evaluation,
        CancellationToken ct)
    {
        try
        {
            await leases.PublishShadowAsync(
                jobId,
                OptimizationEvaluationInputFactory.Create(evaluation),
                clock.GetUtcNow().UtcDateTime,
                ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error,
                "[Optimization] Job {Id}: shadow Worker 임대 발행 실패 — 인프로세스 실행 유지",
                jobId);
        }
    }
}

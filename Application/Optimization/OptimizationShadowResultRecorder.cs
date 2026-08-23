namespace StockTrader.Application.Optimization;

/// <summary>shadow 관측 실패가 authoritative 인프로세스 결과를 변경하지 않게 격리합니다.</summary>
public sealed class OptimizationShadowResultRecorder(
    IOptimizationShadowResultCoordinator comparisons,
    ILogger<OptimizationShadowResultRecorder> logger)
{
    public async Task RecordAsync(int jobId, DateTime observedAt, CancellationToken ct)
    {
        try
        {
            await comparisons.RecordAuthoritativeAsync(jobId, observedAt, ct);
        }
        catch (Exception error)
        {
            logger.LogError(error,
                "Optimization shadow authoritative snapshot failed for Job {JobId}; "
                + "canonical in-process result remains unchanged",
                jobId);
        }
    }
}

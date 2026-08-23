using StockTrader.Application.Optimization;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 우선순위 큐 방식으로 OptimizationJob을 연속 실행하는 BackgroundService.
/// Pending 작업이 없으면 30초 대기 후 재확인합니다.
/// 앱 종료(StoppingToken 취소) 시 현재 청크 완료 후 Pending으로 상태를 되돌립니다.
/// </summary>
public class ContinuousOptimizationService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptimizationWorkExecutor _executor;
    private readonly ILogger<ContinuousOptimizationService> _logger;
    private readonly TimeProvider _clock;

    public ContinuousOptimizationService(
        IServiceScopeFactory scopeFactory,
        IOptimizationWorkExecutor executor,
        ILogger<ContinuousOptimizationService> logger,
        TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ContinuousOptimizationService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            OptimizationJobExecutionTicket? job = null;

            try
            {
                job = await UseLifecycleAsync(
                    lifecycle => lifecycle.TryStartNextAsync(UtcNow));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "다음 Pending Job 시작 실패 — {Delay}후 재시도", PollInterval);
                await Task.Delay(PollInterval, _clock, stoppingToken);
                continue;
            }

            if (job == null)
            {
                _logger.LogDebug("대기 중인 최적화 작업 없음 — {Delay} 대기", PollInterval);
                await Task.Delay(PollInterval, _clock, stoppingToken);
                continue;
            }

            _logger.LogInformation(
                "최적화 작업 시작: Job {Id} ({Name}), Priority={Priority}",
                job.Id, job.Name, job.Priority);

            try
            {
                var disposition = await _executor.ExecuteAsync(job, stoppingToken);
                await PersistExecutionDispositionAsync(job.Id, disposition);

                if (disposition == OptimizationJobExecutionDisposition.Completed)
                    await UseAutoTuneAsync(job.Id, stoppingToken);

                if (disposition == OptimizationJobExecutionDisposition.Completed)
                {
                    _logger.LogInformation(
                        "최적화 작업 완료: Job {Id} ({Name})",
                        job.Id, job.Name);
                }
            }
            catch (OperationCanceledException)
            {
                // 앱 종료 시 → Pending으로 되돌려 재시작 시 이어받을 수 있도록
                _logger.LogWarning(
                    "최적화 작업 중단 (앱 종료): Job {Id} → Pending으로 복귀 (청크 인덱스={Chunk})",
                    job.Id, job.CurrentChunkIndex);

                await ReturnToPendingSafelyAsync(job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "최적화 작업 실패: Job {Id} ({Name})",
                    job.Id, job.Name);

                await MarkFailedSafelyAsync(
                    job.Id,
                    UtcNow,
                    ex.Message);
            }
        }

        _logger.LogInformation("ContinuousOptimizationService 종료");
    }

    private async Task PersistExecutionDispositionAsync(
        int jobId,
        OptimizationJobExecutionDisposition disposition)
    {
        try
        {
            await UseLifecycleAsync(lifecycle =>
                lifecycle.ApplyDispositionAsync(jobId, disposition, UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} 실행 결과 상태 저장 실패", jobId);
        }

        if (disposition == OptimizationJobExecutionDisposition.Paused)
            _logger.LogInformation("최적화 작업 일시정지: Job {Id}", jobId);
        else if (disposition == OptimizationJobExecutionDisposition.Cancelled)
            _logger.LogInformation("최적화 작업 취소: Job {Id}", jobId);
    }

    private async Task ReturnToPendingSafelyAsync(int jobId)
    {
        try
        {
            await UseLifecycleAsync(
                lifecycle => lifecycle.ReturnToPendingAsync(jobId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} Pending 복귀 저장 실패", jobId);
        }
    }

    private async Task MarkFailedSafelyAsync(
        int jobId,
        DateTime failedAt,
        string errorMessage)
    {
        try
        {
            await UseLifecycleAsync(lifecycle =>
                lifecycle.MarkFailedAsync(jobId, failedAt, errorMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} 실패 상태 저장 실패", jobId);
        }
    }

    private async Task<T> UseLifecycleAsync<T>(
        Func<IOptimizationJobLifecycle, Task<T>> action)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lifecycle = scope.ServiceProvider
            .GetRequiredService<IOptimizationJobLifecycle>();
        return await action(lifecycle);
    }

    private async Task UseLifecycleAsync(
        Func<IOptimizationJobLifecycle, Task> action)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lifecycle = scope.ServiceProvider
            .GetRequiredService<IOptimizationJobLifecycle>();
        await action(lifecycle);
    }

    private async Task UseAutoTuneAsync(int jobId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var autoTune = scope.ServiceProvider
            .GetRequiredService<OptimizationAutoTuneService>();
        await autoTune.HandleCompletedJobAsync(jobId, ct);
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
}

using StockTrader.Data.Repositories;
using StockTrader.Models;
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
    private readonly OptimizationJobExecutor _executor;
    private readonly OptimizationAutoTuneService _autoTuneService;
    private readonly ILogger<ContinuousOptimizationService> _logger;
    private readonly TimeProvider _clock;

    public ContinuousOptimizationService(
        IServiceScopeFactory scopeFactory,
        OptimizationJobExecutor executor,
        OptimizationAutoTuneService autoTuneService,
        ILogger<ContinuousOptimizationService> logger,
        TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _autoTuneService = autoTuneService;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ContinuousOptimizationService 시작");

        while (!stoppingToken.IsCancellationRequested)
        {
            OptimizationJob? job = null;

            // scoped repository 접근
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOptimizationRepository>();

            try
            {
                job = await repo.GetNextPendingJobAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "다음 Pending Job 조회 실패 — {Delay}후 재시도", PollInterval);
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

            // Running 상태로 전환
            job.Status    = OptimizationJobStatus.Running;
            job.StartedAt ??= UtcNow;

            try
            {
                await repo.UpdateJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {Id} 상태 업데이트 실패 (Running 전환)", job.Id);
                await Task.Delay(PollInterval, _clock, stoppingToken);
                continue;
            }

            try
            {
                var disposition = await _executor.ExecuteJobAsync(job, stoppingToken);
                await PersistExecutionDispositionAsync(job.Id, disposition);

                if (disposition == OptimizationJobExecutionDisposition.Completed)
                    await _autoTuneService.HandleCompletedJobAsync(job.Id, stoppingToken);

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

                await PersistJobStateAsync(job.Id, OptimizationJobStatus.Pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "최적화 작업 실패: Job {Id} ({Name})",
                    job.Id, job.Name);

                await PersistJobStateAsync(
                    job.Id,
                    OptimizationJobStatus.Failed,
                    completedAt: UtcNow,
                    errorMessage: ex.Message);
            }
        }

        _logger.LogInformation("ContinuousOptimizationService 종료");
    }

    private async Task PersistExecutionDispositionAsync(
        int jobId,
        OptimizationJobExecutionDisposition disposition)
    {
        switch (disposition)
        {
            case OptimizationJobExecutionDisposition.Completed:
                await PersistJobStateAsync(
                    jobId,
                    OptimizationJobStatus.Completed,
                    completedAt: UtcNow,
                    clearErrorMessage: true);
                break;
            case OptimizationJobExecutionDisposition.Paused:
                _logger.LogInformation("최적화 작업 일시정지: Job {Id}", jobId);
                break;
            case OptimizationJobExecutionDisposition.Cancelled:
                await PersistJobStateAsync(
                    jobId,
                    OptimizationJobStatus.Cancelled,
                    completedAt: UtcNow,
                    clearErrorMessage: true);
                _logger.LogInformation("최적화 작업 취소: Job {Id}", jobId);
                break;
        }
    }

    private async Task PersistJobStateAsync(
        int jobId,
        OptimizationJobStatus status,
        DateTime? completedAt = null,
        string? errorMessage = null,
        bool clearErrorMessage = false)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOptimizationRepository>();
            var job = await repo.GetJobSummaryAsync(jobId);
            if (job == null)
                return;

            job.Status = status;
            job.CompletedAt = completedAt;

            if (clearErrorMessage)
                job.ErrorMessage = null;
            else
                job.ErrorMessage = errorMessage;

            await repo.UpdateJobAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} 상태 저장 실패", jobId);
        }
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
}

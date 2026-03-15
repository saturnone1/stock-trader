using StockTrader.Data.Repositories;
using StockTrader.Models;

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
    private readonly ILogger<ContinuousOptimizationService> _logger;

    public ContinuousOptimizationService(
        IServiceScopeFactory scopeFactory,
        OptimizationJobExecutor executor,
        ILogger<ContinuousOptimizationService> logger)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _logger = logger;
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
                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            if (job == null)
            {
                _logger.LogDebug("대기 중인 최적화 작업 없음 — {Delay} 대기", PollInterval);
                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            _logger.LogInformation(
                "최적화 작업 시작: Job {Id} ({Name}), Priority={Priority}",
                job.Id, job.Name, job.Priority);

            // Running 상태로 전환
            job.Status    = OptimizationJobStatus.Running;
            job.StartedAt ??= DateTime.UtcNow;

            try
            {
                await repo.UpdateJobAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {Id} 상태 업데이트 실패 (Running 전환)", job.Id);
                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            try
            {
                await _executor.ExecuteJobAsync(job, stoppingToken);

                // 정상 완료
                job.Status      = OptimizationJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "최적화 작업 완료: Job {Id} ({Name}), 총 {N}건 테스트",
                    job.Id, job.Name, job.TestedCombinations);
            }
            catch (OperationCanceledException)
            {
                // 앱 종료 시 → Pending으로 되돌려 재시작 시 이어받을 수 있도록
                _logger.LogWarning(
                    "최적화 작업 중단 (앱 종료): Job {Id} → Pending으로 복귀 (청크 인덱스={Chunk})",
                    job.Id, job.CurrentChunkIndex);

                job.Status = OptimizationJobStatus.Pending;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "최적화 작업 실패: Job {Id} ({Name})",
                    job.Id, job.Name);

                job.Status       = OptimizationJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt  = DateTime.UtcNow;
            }
            finally
            {
                // 최선을 다해 상태 저장 (실패해도 계속 진행)
                try
                {
                    await using var finalScope = _scopeFactory.CreateAsyncScope();
                    var finalRepo = finalScope.ServiceProvider.GetRequiredService<IOptimizationRepository>();
                    await finalRepo.UpdateJobAsync(job);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {Id} 최종 상태 저장 실패", job.Id);
                }
            }
        }

        _logger.LogInformation("ContinuousOptimizationService 종료");
    }
}

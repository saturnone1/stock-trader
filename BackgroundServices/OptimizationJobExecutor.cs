using StockTrader.Api;
using StockTrader.Application.Optimization;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 단일 OptimizationJob을 실제로 실행하는 Executor.
/// Singleton 수명이며 Scoped 애플리케이션 서비스와 저장소는
/// IServiceScopeFactory로 per-job scope를 생성하여 접근합니다.
/// 준비된 평가 컨텍스트 위에서 청크·저장·중단 상태만 조정합니다.
/// </summary>
public class OptimizationJobExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OptimizationJobExecutor> _logger;
    private readonly TimeProvider _clock;

    public OptimizationJobExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationJobExecutor> logger,
        TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// 작업 하나를 끝까지 (또는 취소/제한 도달까지) 실행합니다.
    /// 청크 단위로 진행되며, 중단되어도 CurrentChunkIndex가 저장되어 재시작 시 이어받을 수 있습니다.
    /// </summary>
    internal async Task<OptimizationJobExecutionDisposition> ExecuteJobAsync(
        OptimizationJobExecutionTicket job,
        CancellationToken ct)
    {
        _logger.LogInformation("[Optimization] Job {Id} ({Name}) 실행 시작 — 청크크기={Chunk}",
            job.Id, job.Name, job.ChunkSize);

        // per-job scope: application use cases and repository
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var contextPreparer = sp.GetRequiredService<IOptimizationEvaluationContextPreparer>();
        var candidateEvaluator = sp.GetRequiredService<IOptimizationCandidateEvaluator>();
        var executionStore = sp.GetRequiredService<IOptimizationJobExecutionStore>();

        // ── 1. RequestJson 역직렬화 ──
        OptimizeRequest request;
        try
        {
            request = OptimizeRequestJsonCodec.Deserialize(job.RequestJson)
                      ?? throw new InvalidOperationException("RequestJson 역직렬화 결과가 null입니다");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"RequestJson 파싱 실패: {ex.Message}", ex);
        }

        // ── 2. IS/OOS 기간 분할 ──
        var period = OptimizationJobExecutionPolicy.SplitPeriod(
            request.From, request.To, request.OosPercent);

        // ── 3. 데이터 피드·레짐·타임프레임별 데이터 1회 준비 ──
        var preparation = await contextPreparer.PrepareAsync(request, ct);
        if (!preparation.IsSuccess)
            throw new InvalidOperationException(preparation.Message);
        var evaluation = preparation.Context!;

        // ── 4. 전체 조합 생성 / TotalCombinations 업데이트 ──
        var allCombinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(request.OptimizeParams);
        if (job.TotalCombinations == 0)
        {
            job.TotalCombinations = allCombinations.Count;
            await executionStore.SaveProgressAsync(
                job.Id,
                job.TestedCombinations,
                job.CurrentChunkIndex,
                job.LastProgressAt,
                job.TotalCombinations);
        }

        var searchPlan = OptimizationJobExecutionPolicy.BuildSearchPlan(
            allCombinations, request.MaxCombinations);
        var stage1Combinations = searchPlan.Stage1Combinations;
        var stage2Budget = searchPlan.Stage2Budget;

        var startedAt   = job.StartedAt ?? UtcNow;

        // ── 5. Stage 1 청크 반복 ──
        int chunkSize   = Math.Max(1, job.ChunkSize);
        int totalChunks = (int)Math.Ceiling(stage1Combinations.Count / (double)chunkSize);
        var stage1Results = new List<OptimizeResultItem>();
        int stage1StartChunk = OptimizationJobExecutionPolicy.CalculateStage1StartChunk(
            job.TestedCombinations,
            stage1Combinations.Count,
            job.CurrentChunkIndex,
            totalChunks);

        for (int chunkIdx = stage1StartChunk; chunkIdx < totalChunks; chunkIdx++)
        {
            ct.ThrowIfCancellationRequested();

            var stopDisposition = await GetExternalStopDispositionAsync(job.Id, executionStore);
            if (stopDisposition.HasValue)
                return stopDisposition.Value;

            if (OptimizationJobExecutionPolicy.HasExceededDuration(
                    startedAt, UtcNow, job.MaxDurationHours))
            {
                _logger.LogInformation(
                    "[Optimization] Job {Id}: MaxDurationHours {H}h 도달, 중단",
                    job.Id, job.MaxDurationHours);
                break;
            }

            if (job.MaxTestedCombinations.HasValue
                && job.TestedCombinations >= job.MaxTestedCombinations.Value)
            {
                _logger.LogInformation(
                    "[Optimization] Job {Id}: MaxTestedCombinations {N} 도달, 중단",
                    job.Id, job.MaxTestedCombinations);
                break;
            }

            var sliceStart = chunkIdx * chunkSize;
            var sliceEnd   = Math.Min(sliceStart + chunkSize, stage1Combinations.Count);
            var chunk      = stage1Combinations.GetRange(sliceStart, sliceEnd - sliceStart);

            var chunkResults = await candidateEvaluator.EvaluateBatchAsync(
                evaluation,
                chunk,
                request.From,
                period.InSampleTo,
                "[Optimization] 조합 백테스트 실패 — 건너뜀",
                ct);

            stage1Results.AddRange(chunkResults);

            var testedAtStart = job.TestedCombinations;
            job.TestedCombinations += chunk.Count;
            job.CurrentChunkIndex   = chunkIdx + 1;
            job.LastProgressAt      = UtcNow;
            await executionStore.SaveChunkAsync(
                job.Id,
                chunkResults,
                testedAtStart,
                job.TestedCombinations,
                job.CurrentChunkIndex,
                job.LastProgressAt.Value,
                job.TopResultsToKeep,
                job.RankBy);

            _logger.LogDebug(
                "[Optimization] Job {Id}: 청크 {C}/{T} 완료, 누적 {N}건",
                job.Id, chunkIdx + 1, totalChunks, job.TestedCombinations);
        }

        // ── 6. Stage 2: 상위 5개 이웃 탐색 ──
        if (stage2Budget > 0)
        {
                var neighbors = await BuildStage2NeighborsAsync(
                    job,
                    executionStore,
                    request,
                    stage1Results,
                    allCombinations,
                    stage1Combinations,
                    stage2Budget);

            if (neighbors.Count > 0)
            {
                _logger.LogInformation(
                    "[Optimization] Job {Id}: Stage 2 정밀 탐색 {N}개 이웃",
                    job.Id, neighbors.Count);

                int stage2ChunkCount = (int)Math.Ceiling(neighbors.Count / (double)chunkSize);
                int stage2StartChunk = Math.Min(
                    OptimizationJobExecutionPolicy.CalculateStage2StartChunk(
                        job.TestedCombinations,
                        stage1Combinations.Count,
                        chunkSize),
                    stage2ChunkCount);
                for (int c = stage2StartChunk; c < stage2ChunkCount; c++)
                {
                    ct.ThrowIfCancellationRequested();

                    var stopDisposition = await GetExternalStopDispositionAsync(job.Id, executionStore);
                    if (stopDisposition.HasValue)
                        return stopDisposition.Value;

                    if (OptimizationJobExecutionPolicy.HasExceededDuration(
                            startedAt, UtcNow, job.MaxDurationHours)) break;
                    if (job.MaxTestedCombinations.HasValue
                        && job.TestedCombinations >= job.MaxTestedCombinations.Value) break;

                    var s      = c * chunkSize;
                    var e      = Math.Min(s + chunkSize, neighbors.Count);
                    var chunk2 = neighbors.GetRange(s, e - s);

                    var chunkResults2 = await candidateEvaluator.EvaluateBatchAsync(
                        evaluation,
                        chunk2,
                        request.From,
                        period.InSampleTo,
                        "[Optimization] Stage 2 백테스트 실패 — 건너뜀",
                        ct);

                    var testedAtStart = job.TestedCombinations;
                    job.TestedCombinations += chunk2.Count;
                    job.CurrentChunkIndex   = totalChunks + c + 1;
                    job.LastProgressAt      = UtcNow;
                    await executionStore.SaveChunkAsync(
                        job.Id,
                        chunkResults2,
                        testedAtStart,
                        job.TestedCombinations,
                        job.CurrentChunkIndex,
                        job.LastProgressAt.Value,
                        job.TopResultsToKeep,
                        job.RankBy);
                }
            }
        }

        // ── 7. OOS 검증: DB 상위 N개 재백테스트 ──
        if (period.HasOutOfSample)
        {
            _logger.LogInformation("[Optimization] Job {Id}: OOS 검증 시작", job.Id);

            var topResults = await executionStore.LoadTopCandidatesAsync(
                job.Id, job.TopResultsToKeep);

            foreach (var storedCandidate in topResults)
            {
                ct.ThrowIfCancellationRequested();

                var stopDisposition = await GetExternalStopDispositionAsync(
                    job.Id, executionStore);
                if (stopDisposition.HasValue)
                    return stopDisposition.Value;

                try
                {
                    var oosResult = await candidateEvaluator.RunAsync(
                        evaluation,
                        storedCandidate.Parameters,
                        period.OutOfSampleFrom,
                        period.OutOfSampleTo,
                        "[Optimization] OOS 백테스트 실패 — 건너뜀",
                        ct);
                    if (oosResult is null) continue;

                    var metrics = OptimizationResultProjection.FromBacktest(oosResult);
                    await executionStore.SaveOutOfSampleAsync(
                        storedCandidate.ResultId, metrics);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "[Optimization] Job {Id}: OOS 백테스트 실패 — 건너뜀", job.Id);
                }
            }
        }

        _logger.LogInformation(
            "[Optimization] Job {Id} 완료 — 총 {N}건 테스트",
            job.Id, job.TestedCombinations);
        return OptimizationJobExecutionDisposition.Completed;
    }

    private async Task<OptimizationJobExecutionDisposition?> GetExternalStopDispositionAsync(
        int jobId,
        IOptimizationJobExecutionStore executionStore)
    {
        var signal = await executionStore.GetControlSignalAsync(jobId);
        return signal switch
        {
            OptimizationJobControlSignal.Pause => OptimizationJobExecutionDisposition.Paused,
            OptimizationJobControlSignal.Cancel => OptimizationJobExecutionDisposition.Cancelled,
            _ => null
        };
    }

    private async Task<List<OptimizeParamSnapshot>> BuildStage2NeighborsAsync(
        OptimizationJobExecutionTicket job,
        IOptimizationJobExecutionStore executionStore,
        OptimizeRequest request,
        List<OptimizeResultItem> stage1Results,
        List<OptimizeParamSnapshot> allCombinations,
        List<OptimizeParamSnapshot> stage1Combinations,
        int stage2Budget)
    {
        List<OptimizeParamSnapshot> seeds;
        if (stage1Results.Count >= 3)
        {
            seeds = OptimizationResultRanker.RankOptimizeResults(
                    stage1Results,
                    job.RankBy,
                    OptimizationJobExecutionPolicy.FineSearchSeedCount)
                .Select(r => r.Params)
                .ToList();
        }
        else
        {
            var persistedTopResults = await executionStore.LoadTopCandidatesAsync(
                job.Id,
                OptimizationJobExecutionPolicy.FineSearchSeedCount);
            seeds = persistedTopResults
                .Select(candidate => candidate.Parameters)
                .ToList();
        }

        var neighbors = seeds.Count >= 3
            ? StrategyOptimizationSpace.GenerateNeighborCombinations(
                seeds,
                request.OptimizeParams,
                stage2Budget,
                stage1Combinations)
            : new List<OptimizeParamSnapshot>();

        return OptimizationJobExecutionPolicy.BuildStage2CandidatePool(
            neighbors,
            stage1Combinations,
            allCombinations,
            stage2Budget,
            job.Id);
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
}

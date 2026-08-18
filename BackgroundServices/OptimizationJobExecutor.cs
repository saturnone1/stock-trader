using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Api;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Application.Backtesting;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 단일 OptimizationJob을 실제로 실행하는 Executor.
/// Singleton 수명이며 Scoped 서비스(BacktestService, IOptimizationRepository)는
/// IServiceScopeFactory로 per-job scope를 생성하여 접근합니다.
/// BacktestService의 internal 메서드를 직접 재사용하여 데이터 1회 로드 후 청크 단위로 진행합니다.
/// </summary>
public class OptimizationJobExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OptimizationJobExecutor> _logger;
    private readonly TimeProvider _clock;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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
    internal async Task<OptimizationJobExecutionDisposition> ExecuteJobAsync(OptimizationJob job, CancellationToken ct)
    {
        _logger.LogInformation("[Optimization] Job {Id} ({Name}) 실행 시작 — 청크크기={Chunk}",
            job.Id, job.Name, job.ChunkSize);

        // per-job scope: BacktestService, IOptimizationRepository, IDataFeedServiceFactory
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var backtestService  = sp.GetRequiredService<BacktestService>();
        var dataPreparer     = sp.GetRequiredService<BacktestDataPreparer>();
        var customDetectors  = sp.GetRequiredService<ICustomStrategyDetectorFactory>();
        var patternSettings  = sp.GetRequiredService<IOptions<PatternSettings>>().Value;
        var repo             = sp.GetRequiredService<IOptimizationRepository>();
        var dataFeedFactory  = sp.GetRequiredService<IDataFeedServiceFactory>();

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

        // ── 3. 데이터 피드 및 레짐 맵 1회 준비 ──
        var dataFeed = request.DataSource.HasValue
            ? dataFeedFactory.GetService(request.DataSource.Value)
            : await dataFeedFactory.GetServiceAsync(ct);

        var regimeSymbol = request.DataSource == DataSource.LsSecurities ? "069500" : "SPY";
        var regimeByDate = await backtestService.BuildRegimeMapAsync(
            dataFeed, request.From, request.To, regimeSymbol, ct);

        if (regimeByDate == null)
            throw new InvalidOperationException("레짐 맵 빌드 실패 — SPY/069500 데이터를 확인하세요");

        // ── 4. 심볼 데이터 사전 로드 (타임프레임별) ──
        var timeFramesToLoad = request.OptimizeParams.TimeFrameOptions is { Count: > 0 }
            ? request.OptimizeParams.TimeFrameOptions.Select(tf => (TimeFrame)tf).Distinct().ToList()
            : new List<TimeFrame> { request.TimeFrame };

        var referenceSymbols = customDetectors.Create(request.BasePattern).Strategy.ReferenceSymbols;
        var optimizationSymbols = request.Symbols
            .Concat(referenceSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dataByTimeFrame = new Dictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>>();

        foreach (var tf in timeFramesToLoad)
        {
            var prepared = await dataPreparer.PrepareAsync(
                dataFeed, optimizationSymbols, tf, request.From, request.To,
                patternSettings.CumulativeRsi2, patternSettings.Tqqq200Sma, ct);
            if (prepared.HasData)
                dataByTimeFrame[tf] = prepared.Symbols;
        }

        if (dataByTimeFrame.Count == 0)
            throw new InvalidOperationException("유효한 심볼 데이터 없음 — 데이터 피드/심볼을 확인하세요");

        var fullDataMap = dataByTimeFrame.ContainsKey(request.TimeFrame)
            ? dataByTimeFrame[request.TimeFrame]
            : dataByTimeFrame.Values.First();

        // ── 5. 전체 조합 생성 / TotalCombinations 업데이트 ──
        var allCombinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(request.OptimizeParams);
        if (job.TotalCombinations == 0)
        {
            job.TotalCombinations = allCombinations.Count;
            await repo.UpdateJobProgressAsync(
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

        var riskParams  = backtestService.DefaultRiskParams;
        var startedAt   = job.StartedAt ?? UtcNow;

        // ── 6. Stage 1 청크 반복 ──
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

            var stopDisposition = await GetExternalStopDispositionAsync(job.Id, repo);
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

            var chunkResults = await RunChunkAsync(
                chunk, request, backtestService, customDetectors, fullDataMap, dataByTimeFrame,
                regimeByDate, riskParams, period.InSampleTo, ct);

            stage1Results.AddRange(chunkResults);

            if (chunkResults.Count > 0)
            {
                var dbResults = chunkResults
                    .Select((r, i) => MapToDbResult(
                        r, job.Id, job.TestedCombinations + i, UtcNow))
                    .ToList();
                await repo.MergeResultsAsync(job.Id, dbResults, job.TopResultsToKeep, job.RankBy);
            }

            job.TestedCombinations += chunk.Count;
            job.CurrentChunkIndex   = chunkIdx + 1;
            job.LastProgressAt      = UtcNow;
            await repo.UpdateJobProgressAsync(
                job.Id,
                job.TestedCombinations,
                job.CurrentChunkIndex,
                job.LastProgressAt);

            _logger.LogDebug(
                "[Optimization] Job {Id}: 청크 {C}/{T} 완료, 누적 {N}건",
                job.Id, chunkIdx + 1, totalChunks, job.TestedCombinations);
        }

        // ── 7. Stage 2: 상위 5개 이웃 탐색 ──
        if (stage2Budget > 0)
        {
                var neighbors = await BuildStage2NeighborsAsync(
                    job,
                    repo,
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

                    var stopDisposition = await GetExternalStopDispositionAsync(job.Id, repo);
                    if (stopDisposition.HasValue)
                        return stopDisposition.Value;

                    if (OptimizationJobExecutionPolicy.HasExceededDuration(
                            startedAt, UtcNow, job.MaxDurationHours)) break;
                    if (job.MaxTestedCombinations.HasValue
                        && job.TestedCombinations >= job.MaxTestedCombinations.Value) break;

                    var s      = c * chunkSize;
                    var e      = Math.Min(s + chunkSize, neighbors.Count);
                    var chunk2 = neighbors.GetRange(s, e - s);

                    var chunkResults2 = await RunChunkAsync(
                        chunk2, request, backtestService, customDetectors, fullDataMap, dataByTimeFrame,
                        regimeByDate, riskParams, period.InSampleTo, ct);

                    if (chunkResults2.Count > 0)
                    {
                        var dbResults2 = chunkResults2
                            .Select((r, i) => MapToDbResult(
                                r, job.Id, job.TestedCombinations + i, UtcNow))
                            .ToList();
                        await repo.MergeResultsAsync(job.Id, dbResults2, job.TopResultsToKeep, job.RankBy);
                    }

                    job.TestedCombinations += chunk2.Count;
                    job.CurrentChunkIndex   = totalChunks + c + 1;
                    job.LastProgressAt      = UtcNow;
                    await repo.UpdateJobProgressAsync(
                        job.Id,
                        job.TestedCombinations,
                        job.CurrentChunkIndex,
                        job.LastProgressAt);
                }
            }
        }

        // ── 8. OOS 검증: DB 상위 N개 재백테스트 ──
        if (period.HasOutOfSample)
        {
            _logger.LogInformation("[Optimization] Job {Id}: OOS 검증 시작", job.Id);

            var topResults = await repo.GetResultsAsync(job.Id, job.TopResultsToKeep);

            foreach (var dbResult in topResults)
            {
                ct.ThrowIfCancellationRequested();

                var stopDisposition = await GetExternalStopDispositionAsync(job.Id, repo);
                if (stopDisposition.HasValue)
                    return stopDisposition.Value;

                OptimizeParamSnapshot? snap;
                try
                {
                    snap = JsonSerializer.Deserialize<OptimizeParamSnapshot>(dbResult.ParamsJson, JsonOpts);
                    if (snap == null) continue;
                }
                catch { continue; }

                var patternCopy = StrategyVariantFactory.CloneStrategyDocument(request.BasePattern);
                StrategyVariantFactory.ApplyOptimizeOverrides(patternCopy, snap);
                var oosDetectors = new List<IPatternDetector>
                {
                    customDetectors.Create(patternCopy)
                };

                var comboTf = snap.TimeFrame.HasValue
                    ? (TimeFrame)snap.TimeFrame.Value
                    : request.TimeFrame;
                var comboDataMap = dataByTimeFrame.TryGetValue(comboTf, out var tfm)
                    ? tfm
                    : fullDataMap;

                try
                {
                    var oosResult = await backtestService.RunCoreWithPreloadedDataAsync(
                        request.Symbols, comboDataMap, oosDetectors, regimeByDate,
                        period.OutOfSampleFrom, period.OutOfSampleTo, request.InitialCapital,
                        OptimizationBacktestAssumptions.SlippagePercent,
                        OptimizationBacktestAssumptions.CommissionPerTrade,
                        comboTf, riskParams, null,
                        OptimizationBacktestAssumptions.CostModel, null, null, ct);

                    dbResult.OosTotalReturn     = oosResult.TotalReturnPercent * 100;
                    dbResult.OosSortinoRatio     = oosResult.SortinoRatio;
                    dbResult.OosSharpeRatio      = oosResult.SharpeRatio;
                    dbResult.OosMaxDrawdown      = oosResult.MaxDrawdown * 100;
                    dbResult.OosWinRate          = oosResult.OverallWinRate * 100;
                    dbResult.OosTotalTrades      = oosResult.TotalTrades;
                    dbResult.OosProfitFactor     = oosResult.ProfitFactor;
                    dbResult.OosCalmarRatio      = oosResult.CalmarRatio;
                    dbResult.OosAnnualizedReturn = oosResult.AnnualizedReturn;

                    await repo.UpsertResultAsync(dbResult);
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

    private async Task<List<OptimizeResultItem>> RunChunkAsync(
        List<OptimizeParamSnapshot> chunk,
        OptimizeRequest request,
        BacktestService backtestService,
        ICustomStrategyDetectorFactory customDetectors,
        IReadOnlyDictionary<string, PreparedSymbolData> fullDataMap,
        Dictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>> dataByTimeFrame,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        BacktestRiskParameters riskParams,
        DateTime inSampleTo,
        CancellationToken ct)
    {
        var results = new List<OptimizeResultItem>(chunk.Count);

        foreach (var combo in chunk)
        {
            ct.ThrowIfCancellationRequested();

            var patternCopy = StrategyVariantFactory.CloneStrategyDocument(request.BasePattern);
            StrategyVariantFactory.ApplyOptimizeOverrides(patternCopy, combo);

            var detectors = new List<IPatternDetector>
            {
                customDetectors.Create(patternCopy)
            };

            try
            {
                var comboTf = combo.TimeFrame.HasValue
                    ? (TimeFrame)combo.TimeFrame.Value
                    : request.TimeFrame;
                var comboDataMap = dataByTimeFrame.TryGetValue(comboTf, out var tfMap)
                    ? tfMap
                    : fullDataMap;

                var btResult = await backtestService.RunCoreWithPreloadedDataAsync(
                    request.Symbols, comboDataMap, detectors, regimeByDate,
                    request.From, inSampleTo, request.InitialCapital,
                    OptimizationBacktestAssumptions.SlippagePercent,
                    OptimizationBacktestAssumptions.CommissionPerTrade,
                    comboTf, riskParams, null,
                    OptimizationBacktestAssumptions.CostModel, null, null, ct);

                results.Add(new OptimizeResultItem
                {
                    Params           = combo,
                    TotalReturn      = btResult.TotalReturnPercent * 100,
                    SortinoRatio     = btResult.SortinoRatio,
                    SharpeRatio      = btResult.SharpeRatio,
                    MaxDrawdown      = btResult.MaxDrawdown * 100,
                    WinRate          = btResult.OverallWinRate * 100,
                    TotalTrades      = btResult.TotalTrades,
                    ProfitFactor     = btResult.ProfitFactor,
                    CalmarRatio      = btResult.CalmarRatio,
                    AnnualizedReturn = btResult.AnnualizedReturn,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[Optimization] 조합 백테스트 실패 — 건너뜀");
            }
        }

        return results;
    }

    private static OptimizationResult MapToDbResult(
        OptimizeResultItem item,
        int jobId,
        long testedAtCombination,
        DateTime discoveredAt)
    {
        return new OptimizationResult
        {
            JobId               = jobId,
            ParamsJson          = JsonSerializer.Serialize(item.Params),
            TotalReturn         = item.TotalReturn,
            SortinoRatio        = item.SortinoRatio,
            SharpeRatio         = item.SharpeRatio,
            MaxDrawdown         = item.MaxDrawdown,
            WinRate             = item.WinRate,
            TotalTrades         = item.TotalTrades,
            ProfitFactor        = item.ProfitFactor,
            CalmarRatio         = item.CalmarRatio,
            AnnualizedReturn    = item.AnnualizedReturn,
            TestedAtCombination = testedAtCombination,
            DiscoveredAt        = discoveredAt,
        };
    }

    private async Task<OptimizationJobExecutionDisposition?> GetExternalStopDispositionAsync(
        int jobId,
        IOptimizationRepository repo)
    {
        var status = await repo.GetJobStatusAsync(jobId);
        return status switch
        {
            OptimizationJobStatus.Paused => OptimizationJobExecutionDisposition.Paused,
            OptimizationJobStatus.Cancelled => OptimizationJobExecutionDisposition.Cancelled,
            _ => null
        };
    }

    private async Task<List<OptimizeParamSnapshot>> BuildStage2NeighborsAsync(
        OptimizationJob job,
        IOptimizationRepository repo,
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
            var persistedTopResults = await repo.GetResultsAsync(
                job.Id,
                OptimizationJobExecutionPolicy.FineSearchSeedCount);
            seeds = persistedTopResults
                .Select(r => JsonSerializer.Deserialize<OptimizeParamSnapshot>(r.ParamsJson, JsonOpts))
                .Where(s => s != null)
                .Select(s => s!)
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

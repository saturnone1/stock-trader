using System.Text.Json;
using StockTrader.Api;
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

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public OptimizationJobExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationJobExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 작업 하나를 끝까지 (또는 취소/제한 도달까지) 실행합니다.
    /// 청크 단위로 진행되며, 중단되어도 CurrentChunkIndex가 저장되어 재시작 시 이어받을 수 있습니다.
    /// </summary>
    public async Task ExecuteJobAsync(OptimizationJob job, CancellationToken ct)
    {
        _logger.LogInformation("[Optimization] Job {Id} ({Name}) 실행 시작 — 청크크기={Chunk}",
            job.Id, job.Name, job.ChunkSize);

        // per-job scope: BacktestService, IOptimizationRepository, IDataFeedServiceFactory
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var backtestService  = sp.GetRequiredService<BacktestService>();
        var repo             = sp.GetRequiredService<IOptimizationRepository>();
        var dataFeedFactory  = sp.GetRequiredService<IDataFeedServiceFactory>();

        // ── 1. RequestJson 역직렬화 ──
        OptimizeRequest request;
        try
        {
            request = JsonSerializer.Deserialize<OptimizeRequest>(job.RequestJson, JsonOpts)
                      ?? throw new InvalidOperationException("RequestJson 역직렬화 결과가 null입니다");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"RequestJson 파싱 실패: {ex.Message}", ex);
        }

        // ── 2. IS/OOS 기간 분할 ──
        var oosPercent = Math.Clamp(request.OosPercent, 0m, 0.5m);
        var totalDays  = (request.To - request.From).TotalDays;
        var isTo       = oosPercent > 0
            ? request.From.AddDays(totalDays * (double)(1m - oosPercent))
            : request.To;
        var oosFrom = isTo;
        var oosTo   = request.To;
        var hasOos  = oosPercent > 0 && oosFrom < oosTo;

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

        var indicators = backtestService.Indicators;
        var dataByTimeFrame = new Dictionary<TimeFrame, Dictionary<string, BacktestService.SymbolPreparedData>>();

        foreach (var tf in timeFramesToLoad)
        {
            var warmupDays = tf switch
            {
                TimeFrame.OneMinute     => 2,
                TimeFrame.FiveMinute    => 10,
                TimeFrame.FifteenMinute => 15,
                _                       => 400
            };

            var tfDataMap = new Dictionary<string, BacktestService.SymbolPreparedData>();

            foreach (var symbol in request.Symbols)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var bars = await dataFeed.GetHistoricalBarsAsync(
                        symbol, tf, request.From.AddDays(-warmupDays), request.To, ct);

                    if (bars.Count < TradeSimulator.MinWarmupBars) continue;

                    var barsArray   = bars.ToArray();
                    var atrArray    = indicators.ATR(barsArray, 14);
                    var closesArray = IndicatorService.ExtractCloses(barsArray);
                    var sma200Array = indicators.SMA(closesArray, 200);

                    var dateToIndex = new Dictionary<DateOnly, int>(barsArray.Length);
                    for (int i = 0; i < barsArray.Length; i++)
                        dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                    tfDataMap[symbol] = new BacktestService.SymbolPreparedData(
                        barsArray, atrArray, closesArray, sma200Array, dateToIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Optimization] Job {Id}: {Symbol}/{TF} 데이터 로드 실패 — 건너뜀",
                        job.Id, symbol, tf);
                }
            }

            if (tfDataMap.Count > 0)
                dataByTimeFrame[tf] = tfDataMap;
        }

        if (dataByTimeFrame.Count == 0)
            throw new InvalidOperationException("유효한 심볼 데이터 없음 — 데이터 피드/심볼을 확인하세요");

        var fullDataMap = dataByTimeFrame.ContainsKey(request.TimeFrame)
            ? dataByTimeFrame[request.TimeFrame]
            : dataByTimeFrame.Values.First();

        // ── 5. 전체 조합 생성 / TotalCombinations 업데이트 ──
        var allCombinations = BacktestService.GenerateOptimizeCombinations(request.OptimizeParams);
        if (job.TotalCombinations == 0)
        {
            job.TotalCombinations = allCombinations.Count;
            await repo.UpdateJobAsync(job);
        }

        // Random seed를 job.Id로 고정 → 재시작 시 동일 순서 재현
        var rng = new Random(job.Id);

        List<OptimizeParamSnapshot> stage1Combinations;
        int stage2Budget = 0;
        if (allCombinations.Count <= request.MaxCombinations)
        {
            stage1Combinations = allCombinations;
        }
        else
        {
            var stage1Budget = (int)(request.MaxCombinations * 0.6);
            stage2Budget = request.MaxCombinations - stage1Budget;
            stage1Combinations = allCombinations
                .OrderBy(_ => rng.Next())
                .Take(stage1Budget)
                .ToList();
        }

        var riskParams  = backtestService.DefaultRiskParams;
        var startedAt   = job.StartedAt ?? DateTime.UtcNow;
        var maxDuration = job.MaxDurationHours.HasValue
            ? TimeSpan.FromHours((double)job.MaxDurationHours.Value)
            : TimeSpan.MaxValue;

        // ── 6. Stage 1 청크 반복 ──
        int chunkSize   = Math.Max(1, job.ChunkSize);
        int totalChunks = (int)Math.Ceiling(stage1Combinations.Count / (double)chunkSize);
        var stage1Results = new List<OptimizeResultItem>();

        for (int chunkIdx = job.CurrentChunkIndex; chunkIdx < totalChunks; chunkIdx++)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTime.UtcNow - startedAt > maxDuration)
            {
                _logger.LogInformation(
                    "[Optimization] Job {Id}: MaxDurationHours {H}h 도달, 중단",
                    job.Id, job.MaxDurationHours);
                return;
            }

            if (job.MaxTestedCombinations.HasValue
                && job.TestedCombinations >= job.MaxTestedCombinations.Value)
            {
                _logger.LogInformation(
                    "[Optimization] Job {Id}: MaxTestedCombinations {N} 도달, 중단",
                    job.Id, job.MaxTestedCombinations);
                return;
            }

            var sliceStart = chunkIdx * chunkSize;
            var sliceEnd   = Math.Min(sliceStart + chunkSize, stage1Combinations.Count);
            var chunk      = stage1Combinations.GetRange(sliceStart, sliceEnd - sliceStart);

            var chunkResults = await RunChunkAsync(
                chunk, request, backtestService, fullDataMap, dataByTimeFrame,
                regimeByDate, riskParams, isTo, ct);

            stage1Results.AddRange(chunkResults);

            if (chunkResults.Count > 0)
            {
                var dbResults = chunkResults
                    .Select((r, i) => MapToDbResult(r, job.Id, job.TestedCombinations + i))
                    .ToList();
                await repo.MergeResultsAsync(job.Id, dbResults, job.TopResultsToKeep, job.RankBy);
            }

            job.TestedCombinations += chunk.Count;
            job.CurrentChunkIndex   = chunkIdx + 1;
            job.LastProgressAt      = DateTime.UtcNow;
            await repo.UpdateJobAsync(job);

            _logger.LogDebug(
                "[Optimization] Job {Id}: 청크 {C}/{T} 완료, 누적 {N}건",
                job.Id, chunkIdx + 1, totalChunks, job.TestedCombinations);
        }

        // ── 7. Stage 2: 상위 5개 이웃 탐색 ──
        if (stage2Budget > 0 && stage1Results.Count >= 3)
        {
            var top5 = BacktestService.RankOptimizeResults(stage1Results, job.RankBy, 5);
            var neighbors = BacktestService.GenerateNeighborCombinations(
                top5.Select(r => r.Params).ToList(),
                request.OptimizeParams,
                stage2Budget,
                stage1Combinations);

            _logger.LogInformation(
                "[Optimization] Job {Id}: Stage 2 정밀 탐색 {N}개 이웃",
                job.Id, neighbors.Count);

            int stage2ChunkCount = (int)Math.Ceiling(neighbors.Count / (double)chunkSize);
            for (int c = 0; c < stage2ChunkCount; c++)
            {
                ct.ThrowIfCancellationRequested();

                if (DateTime.UtcNow - startedAt > maxDuration) return;
                if (job.MaxTestedCombinations.HasValue
                    && job.TestedCombinations >= job.MaxTestedCombinations.Value) return;

                var s      = c * chunkSize;
                var e      = Math.Min(s + chunkSize, neighbors.Count);
                var chunk2 = neighbors.GetRange(s, e - s);

                var chunkResults2 = await RunChunkAsync(
                    chunk2, request, backtestService, fullDataMap, dataByTimeFrame,
                    regimeByDate, riskParams, isTo, ct);

                if (chunkResults2.Count > 0)
                {
                    var dbResults2 = chunkResults2
                        .Select((r, i) => MapToDbResult(r, job.Id, job.TestedCombinations + i))
                        .ToList();
                    await repo.MergeResultsAsync(job.Id, dbResults2, job.TopResultsToKeep, job.RankBy);
                }

                job.TestedCombinations += chunk2.Count;
                job.LastProgressAt      = DateTime.UtcNow;
                await repo.UpdateJobAsync(job);
            }
        }

        // ── 8. OOS 검증: DB 상위 N개 재백테스트 ──
        if (hasOos)
        {
            _logger.LogInformation("[Optimization] Job {Id}: OOS 검증 시작", job.Id);

            var topResults = await repo.GetResultsAsync(job.Id, job.TopResultsToKeep);

            foreach (var dbResult in topResults)
            {
                ct.ThrowIfCancellationRequested();

                OptimizeParamSnapshot? snap;
                try
                {
                    snap = JsonSerializer.Deserialize<OptimizeParamSnapshot>(dbResult.ParamsJson, JsonOpts);
                    if (snap == null) continue;
                }
                catch { continue; }

                var patternCopy = BacktestService.ClonePatternDefinition(request.BasePattern);
                BacktestService.ApplyOptimizeOverrides(patternCopy, snap);
                var oosDetectors = new List<IPatternDetector>
                {
                    new RuleBasedDetector(indicators, patternCopy)
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
                        oosFrom, oosTo, request.InitialCapital,
                        0.05m, 1.00m, comboTf, riskParams,
                        null, SlippageModel.Adaptive, null, ct);

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
    }

    // ── 청크 단위 백테스트 실행 ──────────────────────────────────────────────────

    private async Task<List<OptimizeResultItem>> RunChunkAsync(
        List<OptimizeParamSnapshot> chunk,
        OptimizeRequest request,
        BacktestService backtestService,
        Dictionary<string, BacktestService.SymbolPreparedData> fullDataMap,
        Dictionary<TimeFrame, Dictionary<string, BacktestService.SymbolPreparedData>> dataByTimeFrame,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        BacktestService.RiskParams riskParams,
        DateTime isTo,
        CancellationToken ct)
    {
        var results = new List<OptimizeResultItem>(chunk.Count);

        foreach (var combo in chunk)
        {
            ct.ThrowIfCancellationRequested();

            var patternCopy = BacktestService.ClonePatternDefinition(request.BasePattern);
            BacktestService.ApplyOptimizeOverrides(patternCopy, combo);

            var detectors = new List<IPatternDetector>
            {
                new RuleBasedDetector(backtestService.Indicators, patternCopy)
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
                    request.From, isTo, request.InitialCapital,
                    0.05m, 1.00m, comboTf, riskParams,
                    null, SlippageModel.Adaptive, null, ct);

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

    // ── 매핑 헬퍼 ────────────────────────────────────────────────────────────────

    private static OptimizationResult MapToDbResult(
        OptimizeResultItem item, int jobId, long testedAtCombination)
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
            DiscoveredAt        = DateTime.UtcNow,
        };
    }
}

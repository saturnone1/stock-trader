using System.Diagnostics;
using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>파라미터 후보 준비, IS/OOS 실행과 결과 랭킹을 조정하는 최적화 유스케이스입니다.</summary>
public sealed class BacktestOptimizationService
{
    private readonly IDataFeedServiceFactory _dataFeeds;
    private readonly ICustomStrategyDetectorFactory _detectors;
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly IOptimizationCandidateEvaluator _candidateEvaluator;
    private readonly BacktestRegimeMapBuilder _regimes;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _patternSettings;
    private readonly ILogger<BacktestOptimizationService> _logger;

    public BacktestOptimizationService(
        IDataFeedServiceFactory dataFeeds,
        ICustomStrategyDetectorFactory detectors,
        BacktestDataPreparer dataPreparer,
        IOptimizationCandidateEvaluator candidateEvaluator,
        BacktestRegimeMapBuilder regimes,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ILogger<BacktestOptimizationService> logger)
    {
        _dataFeeds = dataFeeds;
        _detectors = detectors;
        _dataPreparer = dataPreparer;
        _candidateEvaluator = candidateEvaluator;
        _regimes = regimes;
        _tradingSettings = tradingSettings.Value;
        _patternSettings = patternSettings.Value;
        _logger = logger;
    }

    public async Task<OptimizeResponse> RunAsync(
        OptimizeRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var allCombinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(
            request.OptimizeParams);
        var totalCombinations = allCombinations.Count;
        var searchPlan = OptimizationJobExecutionPolicy.BuildSearchPlan(
            allCombinations, request.MaxCombinations);
        var coarseCombinations = searchPlan.Stage1Combinations;
        var fineBudget = searchPlan.Stage2Budget;

        _logger.LogInformation(
            "파라미터 최적화 시작: 총 {Total}개 조합, Stage1={Stage1}개, Stage2 예산={Stage2}개, 심볼={Symbols}",
            totalCombinations,
            coarseCombinations.Count,
            fineBudget,
            string.Join(",", request.Symbols));

        var period = OptimizationJobExecutionPolicy.SplitPeriod(
            request.From, request.To, request.OosPercent);
        var dataFeed = request.DataSource.HasValue
            ? _dataFeeds.GetService(request.DataSource.Value)
            : await _dataFeeds.GetServiceAsync(ct);
        var regimeSymbol = request.DataSource == DataSource.LsSecurities ? "069500" : "SPY";
        var regimeByDate = await _regimes.BuildAsync(
            dataFeed, request.From, request.To, regimeSymbol, ct);
        if (regimeByDate is null)
            return Empty(totalCombinations, stopwatch.ElapsedMilliseconds);

        var dataByTimeFrame = await PrepareDataAsync(request, dataFeed, ct);
        if (dataByTimeFrame.Count == 0)
        {
            _logger.LogWarning("최적화: 유효한 심볼 데이터 없음");
            return Empty(totalCombinations, stopwatch.ElapsedMilliseconds);
        }

        var defaultData = dataByTimeFrame.TryGetValue(request.TimeFrame, out var requestedData)
            ? requestedData
            : dataByTimeFrame.Values.First();
        var risk = new OptimizationRiskParameters(
            _tradingSettings.RiskPerTradePercent,
            _tradingSettings.DailyLossLimitPercent,
            _tradingSettings.MaxTotalPositions,
            _tradingSettings.MaxPositionsPerSector);
        var evaluation = new OptimizationEvaluationContext(
            request,
            dataByTimeFrame,
            defaultData,
            regimeByDate,
            risk);
        var results = new List<OptimizeResultItem>(coarseCombinations.Count + fineBudget);
        results.AddRange(await _candidateEvaluator.EvaluateBatchAsync(
            evaluation,
            coarseCombinations,
            request.From,
            period.InSampleTo,
            "최적화 조합 백테스트 실패 — 건너뜀",
            ct));

        if (fineBudget > 0 && results.Count >= 3)
        {
            var seeds = OptimizationResultRanker.RankOptimizeResults(
                results,
                request.RankBy,
                OptimizationJobExecutionPolicy.FineSearchSeedCount);
            var neighbors = StrategyOptimizationSpace.GenerateNeighborCombinations(
                seeds.Select(result => result.Params).ToList(),
                request.OptimizeParams,
                fineBudget,
                allCombinations);
            _logger.LogInformation(
                "Stage 2 정밀 탐색: {Count}개 이웃 조합 테스트", neighbors.Count);

            results.AddRange(await _candidateEvaluator.EvaluateBatchAsync(
                evaluation,
                neighbors,
                request.From,
                period.InSampleTo,
                "Stage 2 백테스트 실패",
                ct));
        }

        var ranked = OptimizationResultRanker.RankOptimizeResults(
            results, request.RankBy, request.MaxResults);
        if (period.HasOutOfSample)
        {
            foreach (var item in ranked)
            {
                var oos = await _candidateEvaluator.RunAsync(
                    evaluation,
                    item.Params,
                    period.OutOfSampleFrom,
                    period.OutOfSampleTo,
                    "OOS 백테스트 실패",
                    ct);
                if (oos is not null)
                    OptimizationResultProjection.ApplyOutOfSample(item, oos);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "파라미터 최적화 완료: {Tested}개 테스트, {ElapsedMs}ms",
            results.Count,
            stopwatch.ElapsedMilliseconds);
        return new OptimizeResponse
        {
            TotalCombinations = totalCombinations,
            TestedCombinations = results.Count,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Results = ranked,
            IsFrom = request.From,
            IsTo = period.InSampleTo,
            OosFrom = period.HasOutOfSample ? period.OutOfSampleFrom : null,
            OosTo = period.HasOutOfSample ? period.OutOfSampleTo : null
        };
    }

    private async Task<Dictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>>> PrepareDataAsync(
        OptimizeRequest request,
        IDataFeedService dataFeed,
        CancellationToken ct)
    {
        var timeFrames = request.OptimizeParams.TimeFrameOptions is { Count: > 0 }
            ? request.OptimizeParams.TimeFrameOptions
                .Select(value => (TimeFrame)value)
                .Distinct()
                .ToList()
            : [request.TimeFrame];
        var symbols = request.Symbols
            .Concat(BacktestDetectorMetadata.CollectReferenceSymbols(
                [_detectors.Create(request.BasePattern)]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new Dictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>>();
        foreach (var timeFrame in timeFrames)
        {
            var prepared = await _dataPreparer.PrepareAsync(
                dataFeed,
                symbols,
                timeFrame,
                request.From,
                request.To,
                _patternSettings.CumulativeRsi2,
                _patternSettings.Tqqq200Sma,
                ct);
            if (prepared.HasData) result[timeFrame] = prepared.Symbols;
        }
        return result;
    }

    private static OptimizeResponse Empty(int total, long elapsed) => new()
    {
        TotalCombinations = total,
        TestedCombinations = 0,
        ElapsedMs = elapsed
    };
}

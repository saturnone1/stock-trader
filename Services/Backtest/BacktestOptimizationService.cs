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
    private const decimal OptimizationSlippagePercent = 0.05m;
    private const decimal OptimizationCommissionPerTrade = 1.00m;
    private const decimal CoarseSearchFraction = 0.60m;
    private const int FineSearchSeedCount = 5;

    private readonly IDataFeedServiceFactory _dataFeeds;
    private readonly ICustomStrategyDetectorFactory _detectors;
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestPreparedSimulationRunner _runner;
    private readonly BacktestRegimeMapBuilder _regimes;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _patternSettings;
    private readonly ILogger<BacktestOptimizationService> _logger;

    public BacktestOptimizationService(
        IDataFeedServiceFactory dataFeeds,
        ICustomStrategyDetectorFactory detectors,
        BacktestDataPreparer dataPreparer,
        BacktestPreparedSimulationRunner runner,
        BacktestRegimeMapBuilder regimes,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ILogger<BacktestOptimizationService> logger)
    {
        _dataFeeds = dataFeeds;
        _detectors = detectors;
        _dataPreparer = dataPreparer;
        _runner = runner;
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
        var (coarseCombinations, fineBudget) = SelectSearchStages(
            allCombinations, request.MaxCombinations);

        _logger.LogInformation(
            "파라미터 최적화 시작: 총 {Total}개 조합, Stage1={Stage1}개, Stage2 예산={Stage2}개, 심볼={Symbols}",
            totalCombinations,
            coarseCombinations.Count,
            fineBudget,
            string.Join(",", request.Symbols));

        var oosPercent = Math.Clamp(request.OosPercent, 0m, 0.5m);
        var totalDays = (request.To - request.From).TotalDays;
        var isTo = oosPercent > 0
            ? request.From.AddDays(totalDays * (double)(1m - oosPercent))
            : request.To;
        var oosFrom = isTo;
        var hasOos = oosPercent > 0 && oosFrom < request.To;
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
        var risk = new BacktestRiskParameters(
            _tradingSettings.RiskPerTradePercent,
            _tradingSettings.DailyLossLimitPercent,
            _tradingSettings.MaxTotalPositions,
            _tradingSettings.MaxPositionsPerSector);
        var results = new List<OptimizeResultItem>(coarseCombinations.Count + fineBudget);

        foreach (var combination in coarseCombinations)
        {
            var item = await TryEvaluateAsync(
                request,
                combination,
                dataByTimeFrame,
                defaultData,
                regimeByDate,
                request.From,
                isTo,
                risk,
                "최적화 조합 백테스트 실패 — 건너뜀",
                ct);
            if (item is not null) results.Add(item);
        }

        if (fineBudget > 0 && results.Count >= 3)
        {
            var seeds = OptimizationResultRanker.RankOptimizeResults(
                results, request.RankBy, FineSearchSeedCount);
            var neighbors = StrategyOptimizationSpace.GenerateNeighborCombinations(
                seeds.Select(result => result.Params).ToList(),
                request.OptimizeParams,
                fineBudget,
                allCombinations);
            _logger.LogInformation(
                "Stage 2 정밀 탐색: {Count}개 이웃 조합 테스트", neighbors.Count);

            foreach (var combination in neighbors)
            {
                var item = await TryEvaluateAsync(
                    request,
                    combination,
                    dataByTimeFrame,
                    defaultData,
                    regimeByDate,
                    request.From,
                    isTo,
                    risk,
                    "Stage 2 백테스트 실패",
                    ct);
                if (item is not null) results.Add(item);
            }
        }

        var ranked = OptimizationResultRanker.RankOptimizeResults(
            results, request.RankBy, request.MaxResults);
        if (hasOos)
        {
            foreach (var item in ranked)
            {
                var oos = await TryRunAsync(
                    request,
                    item.Params,
                    dataByTimeFrame,
                    defaultData,
                    regimeByDate,
                    oosFrom,
                    request.To,
                    risk,
                    "OOS 백테스트 실패",
                    ct);
                if (oos is not null) ApplyOos(item, oos);
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
            IsTo = isTo,
            OosFrom = hasOos ? oosFrom : null,
            OosTo = hasOos ? request.To : null
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

    private async Task<OptimizeResultItem?> TryEvaluateAsync(
        OptimizeRequest request,
        OptimizeParamSnapshot combination,
        IReadOnlyDictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>> data,
        IReadOnlyDictionary<string, PreparedSymbolData> defaultData,
        Dictionary<DateOnly, MarketRegime> regimes,
        DateTime from,
        DateTime to,
        BacktestRiskParameters risk,
        string failureMessage,
        CancellationToken ct)
    {
        var result = await TryRunAsync(
            request, combination, data, defaultData, regimes,
            from, to, risk, failureMessage, ct);
        return result is null ? null : ToItem(combination, result);
    }

    private async Task<BacktestResult?> TryRunAsync(
        OptimizeRequest request,
        OptimizeParamSnapshot combination,
        IReadOnlyDictionary<TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>> data,
        IReadOnlyDictionary<string, PreparedSymbolData> defaultData,
        Dictionary<DateOnly, MarketRegime> regimes,
        DateTime from,
        DateTime to,
        BacktestRiskParameters risk,
        string failureMessage,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pattern = StrategyVariantFactory.ClonePatternDefinition(request.BasePattern);
        StrategyVariantFactory.ApplyOptimizeOverrides(pattern, combination);
        var timeFrame = combination.TimeFrame.HasValue
            ? (TimeFrame)combination.TimeFrame.Value
            : request.TimeFrame;
        var prepared = data.TryGetValue(timeFrame, out var selected) ? selected : defaultData;
        try
        {
            return await _runner.RunAsync(
                request.Symbols,
                prepared,
                [_detectors.Create(pattern)],
                regimes,
                from,
                to,
                request.InitialCapital,
                OptimizationSlippagePercent,
                OptimizationCommissionPerTrade,
                timeFrame,
                risk,
                null,
                SlippageModel.Adaptive,
                null,
                _patternSettings,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, failureMessage);
            return null;
        }
    }

    private static (List<OptimizeParamSnapshot> Coarse, int FineBudget) SelectSearchStages(
        List<OptimizeParamSnapshot> all,
        int maximum)
    {
        if (all.Count <= maximum) return (all, 0);
        var coarseBudget = (int)(maximum * CoarseSearchFraction);
        return (
            StrategyOptimizationSpace.SelectDeterministicSample(all, coarseBudget),
            maximum - coarseBudget);
    }

    private static OptimizeResultItem ToItem(
        OptimizeParamSnapshot parameters,
        BacktestResult result) => new()
    {
        Params = parameters,
        TotalReturn = result.TotalReturnPercent * 100,
        SortinoRatio = result.SortinoRatio,
        SharpeRatio = result.SharpeRatio,
        MaxDrawdown = result.MaxDrawdown * 100,
        WinRate = result.OverallWinRate * 100,
        TotalTrades = result.TotalTrades,
        ProfitFactor = result.ProfitFactor,
        CalmarRatio = result.CalmarRatio,
        AnnualizedReturn = result.AnnualizedReturn
    };

    private static void ApplyOos(OptimizeResultItem item, BacktestResult result)
    {
        item.OosTotalReturn = result.TotalReturnPercent * 100;
        item.OosSortinoRatio = result.SortinoRatio;
        item.OosSharpeRatio = result.SharpeRatio;
        item.OosMaxDrawdown = result.MaxDrawdown * 100;
        item.OosWinRate = result.OverallWinRate * 100;
        item.OosTotalTrades = result.TotalTrades;
        item.OosProfitFactor = result.ProfitFactor;
        item.OosCalmarRatio = result.CalmarRatio;
        item.OosAnnualizedReturn = result.AnnualizedReturn;
    }

    private static OptimizeResponse Empty(int total, long elapsed) => new()
    {
        TotalCombinations = total,
        TestedCombinations = 0,
        ElapsedMs = elapsed
    };
}

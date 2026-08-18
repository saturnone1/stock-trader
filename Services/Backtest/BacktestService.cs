using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>백테스트 요청의 데이터, 실행, 검증 분석을 조정하는 애플리케이션 서비스입니다.</summary>
public class BacktestService : IBacktestService
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IBuiltInPatternDetectorFactory _builtInDetectors;
    private readonly ICustomStrategyDetectorFactory _customDetectors;
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestSimulationEngine _simulationEngine;
    private readonly BacktestPreparedSimulationRunner _preparedRunner;
    private readonly BacktestRegimeMapBuilder _regimeMapBuilder;
    private readonly BacktestOptimizationService _optimization;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _basePatternSettings;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        IDataFeedServiceFactory dataFeedFactory,
        IBuiltInPatternDetectorFactory builtInDetectors,
        ICustomStrategyDetectorFactory customDetectors,
        BacktestDataPreparer dataPreparer,
        BacktestSimulationEngine simulationEngine,
        BacktestPreparedSimulationRunner preparedRunner,
        BacktestRegimeMapBuilder regimeMapBuilder,
        BacktestOptimizationService optimization,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ILogger<BacktestService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _builtInDetectors = builtInDetectors;
        _customDetectors = customDetectors;
        _dataPreparer = dataPreparer;
        _simulationEngine = simulationEngine;
        _preparedRunner = preparedRunner;
        _regimeMapBuilder = regimeMapBuilder;
        _optimization = optimization;
        _tradingSettings = tradingSettings.Value;
        _basePatternSettings = patternSettings.Value;
        _logger = logger;
    }

    internal BacktestRiskParameters DefaultRiskParams => new(
        _tradingSettings.RiskPerTradePercent,
        _tradingSettings.DailyLossLimitPercent,
        _tradingSettings.MaxTotalPositions,
        _tradingSettings.MaxPositionsPerSector);

    public async Task<BacktestResult> RunAsync(
        BacktestRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "백테스트 시작: {Symbols} ({From:d} ~ {To:d}) [타임프레임: {TimeFrame}]",
            string.Join(", ", request.Symbols), request.From, request.To, request.TimeFrame);
        var feedSelection = await _dataFeedFactory.SelectAsync(request.DataSource, ct);
        var dataFeed = feedSelection.Service;
        var regimeSymbol = MarketRegimeBenchmarkPolicy.Resolve(feedSelection.Source);
        var regimes = await BuildRegimeMapAsync(
            dataFeed, request.From, request.To, regimeSymbol, ct);
        if (regimes is null) return new BacktestResult();

        var patternSettings = ResolvePatternSettings(request.ParameterOverrides);
        var activeDetectors = BuildDetectors(
            request.Patterns, request.ParameterOverrides, request.CustomPatterns);
        if (activeDetectors.Count == 0)
        {
            _logger.LogWarning("선택된 패턴이 없습니다");
            return new BacktestResult();
        }

        var risk = new BacktestRiskParameters(
            request.RiskPerTradePercent ?? _tradingSettings.RiskPerTradePercent,
            request.DailyLossLimitPercent ?? _tradingSettings.DailyLossLimitPercent,
            request.MaxTotalPositions ?? _tradingSettings.MaxTotalPositions,
            request.MaxPositionsPerSector ?? _tradingSettings.MaxPositionsPerSector);
        var result = await RunCoreAsync(
            request.Symbols,
            dataFeed,
            activeDetectors,
            regimes,
            request.From,
            request.To,
            request.InitialCapital,
            request.SlippagePercent,
            request.CommissionPerTrade,
            request.TimeFrame,
            risk,
            request.ParameterOverrides,
            request.SlippageModel,
            request.WeightStrategy,
            patternSettings,
            ct);
        result.UsedTimeFrame = request.TimeFrame;

        if (request.EnableWalkForward)
        {
            result.WalkForward = await RunWalkForwardAsync(
                request, dataFeed, activeDetectors, regimes, risk, ct);
        }
        if (request.EnableMonteCarlo && result.Trades.Count >= 2)
        {
            var cycles = PerformanceCalculator.AggregateTradeCycles(result.Trades);
            if (cycles.Count >= 2)
            {
                result.MonteCarlo = MonteCarloSimulator.Run(
                    cycles, request.InitialCapital, request.MonteCarloSimulations);
            }
        }

        _logger.LogInformation(
            "백테스트 완료: {Trades}건 거래, 수익률 {Return:P2}, 최대 낙폭 {Drawdown:P2}, 샤프 비율 {Sharpe:F2}",
            result.TotalTrades, result.TotalReturnPercent, result.MaxDrawdown, result.SharpeRatio);
        return result;
    }

    internal async Task<BacktestResult> RunCoreAsync(
        List<string> symbols,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from,
        DateTime to,
        decimal initialCapital,
        decimal slippagePercent,
        decimal commissionPerTrade,
        TimeFrame timeFrame = TimeFrame.Daily,
        BacktestRiskParameters? riskParams = null,
        PatternParameterOverrides? exitOverrides = null,
        SlippageModel slippageModel = SlippageModel.Adaptive,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        riskParams ??= DefaultRiskParams;
        effectivePatternSettings ??= ResolvePatternSettings(exitOverrides);
        var cumulativeRsi2 = effectivePatternSettings.CumulativeRsi2;
        var symbolsToLoad = symbols
            .Concat(BacktestDetectorMetadata.CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = await _dataPreparer.PrepareAsync(
            dataFeed,
            symbolsToLoad,
            timeFrame,
            from,
            to,
            cumulativeRsi2,
            effectivePatternSettings.Tqqq200Sma,
            ct);
        if (!prepared.HasData)
        {
            _logger.LogWarning("유효한 심볼 데이터가 없습니다");
            return new BacktestResult { Warnings = prepared.Warnings.ToList() };
        }

        return await _simulationEngine.RunAsync(
            symbols,
            prepared.Symbols,
            detectors,
            regimeByDate,
            from,
            to,
            initialCapital,
            slippagePercent,
            commissionPerTrade,
            timeFrame,
            riskParams,
            exitOverrides,
            slippageModel,
            prepared.Warnings.ToList(),
            prepared.ActualDataFrom,
            new BacktestExecutionAdapter(),
            weightStrategy,
            cumulativeRsi2,
            ct);
    }

    internal Task<BacktestResult> RunCoreWithPreloadedDataAsync(
        List<string> symbols,
        IReadOnlyDictionary<string, PreparedSymbolData> fullDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from,
        DateTime to,
        decimal initialCapital,
        decimal slippagePercent,
        decimal commissionPerTrade,
        TimeFrame timeFrame,
        BacktestRiskParameters riskParams,
        PatternParameterOverrides? exitOverrides,
        SlippageModel slippageModel,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default) =>
        _preparedRunner.RunAsync(
            symbols, fullDataMap, detectors, regimeByDate,
            from, to, initialCapital, slippagePercent, commissionPerTrade,
            timeFrame, riskParams, exitOverrides, slippageModel,
            weightStrategy, effectivePatternSettings, ct);

    internal Task<Dictionary<DateOnly, MarketRegime>?> BuildRegimeMapAsync(
        IDataFeedService dataFeed,
        DateTime from,
        DateTime to,
        string regimeSymbol = MarketRegimeBenchmarkPolicy.UnitedStatesBenchmark,
        CancellationToken ct = default) =>
        _regimeMapBuilder.BuildAsync(dataFeed, from, to, regimeSymbol, ct);

    internal List<IPatternDetector> BuildDetectors(
        List<PatternType> patterns,
        PatternParameterOverrides? overrides,
        List<StrategyDocument>? customPatterns = null)
    {
        var settings = ResolvePatternSettings(overrides);
        var result = _builtInDetectors.CreateAll(settings)
            .Where(detector => patterns.Contains(detector.PatternType))
            .ToList();

        if (customPatterns is not null && patterns.Contains(PatternType.Custom))
        {
            result.AddRange(customPatterns.Select(_customDetectors.Create));
        }
        return result;
    }

    public Task<OptimizeResponse> RunOptimizationAsync(
        OptimizeRequest request,
        CancellationToken ct = default) => _optimization.RunAsync(request, ct);

    private PatternSettings ResolvePatternSettings(PatternParameterOverrides? overrides) =>
        overrides is null
            ? _basePatternSettings
            : PatternOverrideMerger.Merge(_basePatternSettings, overrides);

    private async Task<WalkForwardResult> RunWalkForwardAsync(
        BacktestRequest request,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimes,
        BacktestRiskParameters risk,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Walk-Forward 분석 시작 (IS:{InSample}개월, OOS:{OutOfSample}개월)",
            request.WalkForwardInSampleMonths,
            request.WalkForwardOutOfSampleMonths);
        var settings = ResolvePatternSettings(request.ParameterOverrides);
        var symbols = request.Symbols
            .Concat(BacktestDetectorMetadata.CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = await _dataPreparer.PrepareAsync(
            dataFeed,
            symbols,
            request.TimeFrame,
            request.From,
            request.To,
            settings.CumulativeRsi2,
            settings.Tqqq200Sma,
            ct);
        var windows = new List<WalkForwardWindow>();
        var windowStart = request.From;
        var totalMonths = request.WalkForwardInSampleMonths
            + request.WalkForwardOutOfSampleMonths;
        while (windowStart.AddMonths(totalMonths) <= request.To)
        {
            ct.ThrowIfCancellationRequested();
            var isFrom = windowStart;
            var isTo = windowStart.AddMonths(request.WalkForwardInSampleMonths);
            var oosFrom = isTo;
            var oosTo = isTo.AddMonths(request.WalkForwardOutOfSampleMonths);
            if (oosTo > request.To) oosTo = request.To;
            var inSample = await RunCoreWithPreloadedDataAsync(
                request.Symbols, prepared.Symbols, detectors, regimes,
                isFrom, isTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, risk, request.ParameterOverrides,
                request.SlippageModel, null, settings, ct);
            var outOfSample = await RunCoreWithPreloadedDataAsync(
                request.Symbols, prepared.Symbols, detectors, regimes,
                oosFrom, oosTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, risk, request.ParameterOverrides,
                request.SlippageModel, null, settings, ct);
            windows.Add(new WalkForwardWindow
            {
                InSampleFrom = isFrom,
                InSampleTo = isTo,
                OutOfSampleFrom = oosFrom,
                OutOfSampleTo = oosTo,
                InSampleTrades = inSample.TotalTrades,
                InSampleReturn = inSample.TotalReturn,
                InSampleReturnPercent = inSample.TotalReturnPercent,
                OutOfSampleTrades = outOfSample.TotalTrades,
                OutOfSampleReturn = outOfSample.TotalReturn,
                OutOfSampleReturnPercent = outOfSample.TotalReturnPercent,
                OutOfSampleMaxDrawdown = outOfSample.MaxDrawdown,
                Efficiency = inSample.TotalReturnPercent > 0
                    ? outOfSample.TotalReturnPercent / inSample.TotalReturnPercent
                    : 0
            });
            windowStart = oosTo;
        }

        var totalInSampleReturn = windows.Sum(window => window.InSampleReturnPercent);
        var totalOutOfSampleReturn = windows.Sum(window => window.OutOfSampleReturnPercent);
        var result = new WalkForwardResult
        {
            Windows = windows,
            AggregateOosReturn = windows.Sum(window => window.OutOfSampleReturn),
            AggregateOosReturnPercent = windows.Count > 0
                ? windows.Average(window => window.OutOfSampleReturnPercent)
                : 0,
            AggregateOosMaxDrawdown = windows.Count > 0
                ? windows.Max(window => window.OutOfSampleMaxDrawdown)
                : 0,
            AggregateOosWinRate = windows.Count > 0
                ? (decimal)windows.Count(window => window.OutOfSampleReturnPercent > 0) / windows.Count
                : 0,
            AggregateOosSharpe = 0,
            WalkForwardEfficiency = totalInSampleReturn > 0
                ? totalOutOfSampleReturn / totalInSampleReturn
                : 0
        };
        _logger.LogInformation(
            "Walk-Forward 완료: {Count}개 윈도우, OOS 평균 수익률 {Average:P2}, WF 효율 {Efficiency:P2}",
            windows.Count,
            result.AggregateOosReturnPercent,
            result.WalkForwardEfficiency);
        return result;
    }

}

using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.Backtesting;
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
    private readonly WalkForwardAnalysisRunner _walkForward;
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
        WalkForwardAnalysisRunner walkForward,
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
        _walkForward = walkForward;
        _regimeMapBuilder = regimeMapBuilder;
        _optimization = optimization;
        _tradingSettings = tradingSettings.Value;
        _basePatternSettings = patternSettings.Value;
        _logger = logger;
    }

    private BacktestRiskParameters DefaultRiskParams => new(
        _tradingSettings.RiskPerTradePercent,
        _tradingSettings.DailyLossLimitPercent,
        _tradingSettings.MaxTotalPositions,
        _tradingSettings.MaxPositionsPerSector);

    public async Task<BacktestResult> RunAsync(
        BacktestRequest request,
        CancellationToken ct = default)
    {
        var selectionErrors = BacktestPatternSelectionPolicy.Validate(
            request.Patterns,
            request.CustomPatterns);
        if (selectionErrors.Count > 0)
            return new BacktestResult { ErrorMessage = string.Join(' ', selectionErrors) };

        _logger.LogInformation(
            "백테스트 시작: {Symbols} ({From:d} ~ {To:d}) [타임프레임: {TimeFrame}]",
            string.Join(", ", request.Symbols), request.From, request.To, request.TimeFrame);
        var feedSelection = await _dataFeedFactory.SelectAsync(request.DataSource, ct);
        var dataFeed = feedSelection.Service;
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
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
        var requestedFrom = DateOnly.FromDateTime(request.From);
        var requestedTo = DateOnly.FromDateTime(request.To);
        if (regimes.Any(pair =>
                pair.Key >= requestedFrom
                && pair.Key <= requestedTo
                && MarketRegimeTrendPolicy.IsUnknown(pair.Value)))
        {
            result.Warnings.Add(MarketRegimeTrendPolicy.InsufficientHistoryWarning);
        }
        result.UsedTimeFrame = request.TimeFrame;

        if (request.EnableWalkForward)
        {
            var outcome = await _walkForward.RunAsync(
                request,
                dataFeed,
                activeDetectors,
                regimes,
                risk,
                patternSettings,
                ct);
            result.WalkForward = outcome.Result;
            if (outcome.Warning is not null && !result.Warnings.Contains(outcome.Warning))
                result.Warnings.Add(outcome.Warning);
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

    private async Task<BacktestResult> RunCoreAsync(
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
        SlippageModel slippageModel = BacktestExecutionCatalog.DefaultSlippageModel,
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
            prepared.Evidence,
            new BacktestExecutionAdapter(),
            weightStrategy,
            cumulativeRsi2,
            ct);
    }

    private Task<Dictionary<DateOnly, MarketRegime>?> BuildRegimeMapAsync(
        IDataFeedService dataFeed,
        DateTime from,
        DateTime to,
        string regimeSymbol = DataProviderCatalog.UnitedStatesRegimeBenchmark,
        CancellationToken ct = default) =>
        _regimeMapBuilder.BuildAsync(dataFeed, from, to, regimeSymbol, ct);

    private List<IPatternDetector> BuildDetectors(
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

}

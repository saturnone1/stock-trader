using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

public class BacktestService : IBacktestService
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly IIndicatorService _indicators;
    private readonly BacktestDataPreparer _dataPreparer;
    private readonly BacktestSimulationEngine _simulationEngine;
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _basePatternSettings;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        IDataFeedServiceFactory dataFeedFactory,
        IEnumerable<IPatternDetector> detectors,
        IIndicatorService indicators,
        BacktestDataPreparer dataPreparer,
        BacktestSimulationEngine simulationEngine,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ISettingsRepository settingsRepo,
        ILogger<BacktestService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _detectors = detectors;
        _indicators = indicators;
        _dataPreparer = dataPreparer;
        _simulationEngine = simulationEngine;
        _tradingSettings = tradingSettings.Value;
        _basePatternSettings = patternSettings.Value;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    /// <summary>최적화 실행 시 기본 리스크 파라미터 (appsettings 기반)</summary>
    internal BacktestRiskParameters DefaultRiskParams => new(
        RiskPerTradePercent:    _tradingSettings.RiskPerTradePercent,
        DailyLossLimitPercent:  _tradingSettings.DailyLossLimitPercent,
        MaxTotalPositions:      _tradingSettings.MaxTotalPositions,
        MaxPositionsPerSector:  _tradingSettings.MaxPositionsPerSector
    );

    private PatternSettings ResolvePatternSettings(PatternParameterOverrides? overrides)
        => overrides == null
            ? _basePatternSettings
            : PatternOverrideMerger.Merge(_basePatternSettings, overrides);

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("백테스트 시작: {Symbols} ({From:d} ~ {To:d}) [타임프레임: {TimeFrame}]",
            string.Join(", ", request.Symbols), request.From, request.To, request.TimeFrame);

        var dataFeed = request.DataSource.HasValue
            ? _dataFeedFactory.GetService(request.DataSource.Value)
            : await _dataFeedFactory.GetServiceAsync(ct);
        var regimeSymbol = request.DataSource == DataSource.LsSecurities ? "069500" : "SPY";
        var regimeByDate = await BuildRegimeMapAsync(dataFeed, request.From, request.To, regimeSymbol, ct);
        if (regimeByDate == null) return new BacktestResult();

        var effectivePatternSettings = ResolvePatternSettings(request.ParameterOverrides);
        var activeDetectors = BuildDetectors(request.Patterns, request.ParameterOverrides, request.CustomPatterns);
        if (activeDetectors.Count == 0)
        {
            _logger.LogWarning("선택된 패턴이 없습니다");
            return new BacktestResult();
        }

        var riskParams = new BacktestRiskParameters(
            RiskPerTradePercent: request.RiskPerTradePercent ?? _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: request.DailyLossLimitPercent ?? _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: request.MaxTotalPositions ?? _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: request.MaxPositionsPerSector ?? _tradingSettings.MaxPositionsPerSector
        );

        var result = await RunCoreAsync(
            request.Symbols, dataFeed, activeDetectors, regimeByDate,
            request.From, request.To, request.InitialCapital,
            request.SlippagePercent, request.CommissionPerTrade,
            request.TimeFrame, riskParams, request.ParameterOverrides,
            request.SlippageModel, request.WeightStrategy, effectivePatternSettings, ct);

        result.UsedTimeFrame = request.TimeFrame;

        if (request.EnableWalkForward)
        {
            result.WalkForward = await RunWalkForwardAsync(
                request, dataFeed, activeDetectors, regimeByDate, riskParams, ct);
        }

        if (request.EnableMonteCarlo && result.Trades.Count >= 2)
        {
            var completedTradeCycles = PerformanceCalculator.AggregateTradeCycles(result.Trades);
            if (completedTradeCycles.Count >= 2)
                result.MonteCarlo = MonteCarloSimulator.Run(
                    completedTradeCycles, request.InitialCapital, request.MonteCarloSimulations);
        }

        _logger.LogInformation(
            "백테스트 완료: {Trades}건 거래, 수익률 {Return:P2}, 최대 낙폭 {Drawdown:P2}, 샤프 비율 {Sharpe:F2}",
            result.TotalTrades, result.TotalReturnPercent, result.MaxDrawdown, result.SharpeRatio);

        return result;
    }

    /// <summary>
    /// 포트폴리오 레벨 시뮬레이션: 모든 심볼이 공유 자본을 사용하며,
    /// maxTotalPositions 제한을 크로스-심볼로 적용합니다.
    /// </summary>
    internal async Task<BacktestResult> RunCoreAsync(
        List<string> symbols,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame = TimeFrame.Daily,
        BacktestRiskParameters? riskParams = null,
        PatternParameterOverrides? exitOverrides = null,
        SlippageModel slippageModel = SlippageModel.Adaptive,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        riskParams ??= new BacktestRiskParameters(
            RiskPerTradePercent: _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: _tradingSettings.MaxPositionsPerSector
        );

        effectivePatternSettings ??= ResolvePatternSettings(exitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;

        var symbolsToLoad = symbols
            .Concat(CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = await _dataPreparer.PrepareAsync(
            dataFeed, symbolsToLoad, timeFrame, from, to,
            cumulativeRsi2Config, effectivePatternSettings.Tqqq200Sma, ct);

        if (!prepared.HasData)
        {
            _logger.LogWarning("유효한 심볼 데이터가 없습니다");
            return new BacktestResult { Warnings = prepared.Warnings.ToList() };
        }

        var simulator = new BacktestExecutionAdapter();
        return await _simulationEngine.RunAsync(
            symbols, prepared.Symbols, detectors, regimeByDate,
            from, to, initialCapital, slippagePercent, commissionPerTrade,
            timeFrame, riskParams, exitOverrides, slippageModel,
            prepared.Warnings.ToList(), prepared.ActualDataFrom, simulator,
            weightStrategy, cumulativeRsi2Config, ct);
    }

    /// <summary>
    /// 핵심 시뮬레이션 루프 (Phase 2~3). RunCoreAsync와 RunCoreWithPreloadedDataAsync가 공유.
    /// symbolDataMap이 이미 구성된 상태에서 호출됩니다.
    /// </summary>
    /// <summary>
    /// Walk-Forward 전용 오버로드: 이미 로드된 symbolDataMap에서 날짜 범위를 슬라이싱하여
    /// API 호출 없이 시뮬레이션을 실행합니다.
    /// </summary>
    internal async Task<BacktestResult> RunCoreWithPreloadedDataAsync(
        List<string> symbols,
        IReadOnlyDictionary<string, PreparedSymbolData> fullDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame,
        BacktestRiskParameters riskParams,
        PatternParameterOverrides? exitOverrides,
        SlippageModel slippageModel,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        effectivePatternSettings ??= ResolvePatternSettings(exitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;

        var sliceSymbols = symbols
            .Concat(CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var prepared = _dataPreparer.Slice(
            fullDataMap, sliceSymbols, timeFrame, from, to,
            cumulativeRsi2Config, effectivePatternSettings.Tqqq200Sma);

        if (!prepared.HasData)
            return new BacktestResult { Warnings = prepared.Warnings.ToList() };

        // 이하 RunCoreAsync와 동일한 시뮬레이션 로직 (공통 메서드로 위임)
        var simulator = new BacktestExecutionAdapter();
        return await _simulationEngine.RunAsync(
            symbols, prepared.Symbols, detectors, regimeByDate,
            from, to, initialCapital, slippagePercent, commissionPerTrade,
            timeFrame, riskParams, exitOverrides, slippageModel,
            prepared.Warnings.ToList(), prepared.ActualDataFrom, simulator,
            weightStrategy, cumulativeRsi2Config, ct);
    }

    #region Walk-Forward Analysis

    private async Task<WalkForwardResult> RunWalkForwardAsync(
        BacktestRequest request,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        BacktestRiskParameters riskParams,
        CancellationToken ct)
    {
        _logger.LogInformation("Walk-Forward 분석 시작 (IS:{IS}개월, OOS:{OOS}개월)",
            request.WalkForwardInSampleMonths, request.WalkForwardOutOfSampleMonths);
        var effectivePatternSettings = ResolvePatternSettings(request.ParameterOverrides);

        // ── 전체 기간 데이터 1회 사전 로드 (윈도우마다 API 재호출 방지) ──
        // 일봉 기준 warmup 400일치를 포함하여 충분히 이전 데이터부터 로드
        var walkForwardSymbols = request.Symbols
            .Concat(CollectReferenceSymbols(detectors))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var walkForwardData = await _dataPreparer.PrepareAsync(
            dataFeed, walkForwardSymbols, request.TimeFrame, request.From, request.To,
            effectivePatternSettings.CumulativeRsi2, effectivePatternSettings.Tqqq200Sma, ct);
        var wfFullDataMap = walkForwardData.Symbols;

        _logger.LogInformation("Walk-Forward 사전 데이터 로드 완료: {Count}개 심볼", wfFullDataMap.Count);

        var windows = new List<WalkForwardWindow>();
        var windowStart = request.From;
        var totalMonths = request.WalkForwardInSampleMonths + request.WalkForwardOutOfSampleMonths;

        while (windowStart.AddMonths(totalMonths) <= request.To)
        {
            ct.ThrowIfCancellationRequested();

            var isFrom = windowStart;
            var isTo = windowStart.AddMonths(request.WalkForwardInSampleMonths);
            var oosFrom = isTo;
            var oosTo = isTo.AddMonths(request.WalkForwardOutOfSampleMonths);
            if (oosTo > request.To) oosTo = request.To;

            // 사전 로드된 데이터에서 슬라이싱 (API 재호출 없음)
            var isResult = await RunCoreWithPreloadedDataAsync(
                request.Symbols, wfFullDataMap, detectors, regimeByDate,
                isFrom, isTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, riskParams, request.ParameterOverrides,
                request.SlippageModel, null, effectivePatternSettings, ct);

            var oosResult = await RunCoreWithPreloadedDataAsync(
                request.Symbols, wfFullDataMap, detectors, regimeByDate,
                oosFrom, oosTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, riskParams, request.ParameterOverrides,
                request.SlippageModel, null, effectivePatternSettings, ct);

            // W06 fix: IS가 음수이면 비율이 의미 없음(음수/음수 = 양수 오해 위험).
            // IS 수익률이 양수일 때만 OOS/IS 효율을 계산하고, 그 외는 0으로 처리.
            var efficiency = isResult.TotalReturnPercent > 0
                ? oosResult.TotalReturnPercent / isResult.TotalReturnPercent
                : 0;

            windows.Add(new WalkForwardWindow
            {
                InSampleFrom = isFrom,
                InSampleTo = isTo,
                OutOfSampleFrom = oosFrom,
                OutOfSampleTo = oosTo,
                InSampleTrades = isResult.TotalTrades,
                InSampleReturn = isResult.TotalReturn,
                InSampleReturnPercent = isResult.TotalReturnPercent,
                OutOfSampleTrades = oosResult.TotalTrades,
                OutOfSampleReturn = oosResult.TotalReturn,
                OutOfSampleReturnPercent = oosResult.TotalReturnPercent,
                OutOfSampleMaxDrawdown = oosResult.MaxDrawdown,
                Efficiency = efficiency
            });

            windowStart = oosTo;
        }

        var allOosTrades = windows.Sum(w => w.OutOfSampleTrades);
        var allOosReturn = windows.Sum(w => w.OutOfSampleReturn);
        var totalIsReturn = windows.Sum(w => w.InSampleReturnPercent);
        var totalOosReturn = windows.Sum(w => w.OutOfSampleReturnPercent);
        var avgOosReturnPct = windows.Count > 0
            ? windows.Average(w => w.OutOfSampleReturnPercent) : 0;
        var avgOosMaxDd = windows.Count > 0
            ? windows.Max(w => w.OutOfSampleMaxDrawdown) : 0;
        var oosWinWindows = windows.Count(w => w.OutOfSampleReturnPercent > 0);
        var oosWinRate = windows.Count > 0 ? (decimal)oosWinWindows / windows.Count : 0;
        // W06 fix: 집계 효율도 IS 총 수익이 양수일 때만 의미 있음.
        var wfEfficiency = totalIsReturn > 0 ? totalOosReturn / totalIsReturn : 0;

        _logger.LogInformation(
            "Walk-Forward 완료: {Count}개 윈도우, OOS 평균 수익률 {Avg:P2}, WF 효율 {Eff:P2}",
            windows.Count, avgOosReturnPct, wfEfficiency);

        return new WalkForwardResult
        {
            Windows = windows,
            AggregateOosReturn = allOosReturn,
            AggregateOosReturnPercent = avgOosReturnPct,
            AggregateOosMaxDrawdown = avgOosMaxDd,
            AggregateOosWinRate = oosWinRate,
            AggregateOosSharpe = 0,
            WalkForwardEfficiency = wfEfficiency
        };
    }

    #endregion

    #region Regime Map

    internal async Task<Dictionary<DateOnly, MarketRegime>?> BuildRegimeMapAsync(
        IDataFeedService dataFeed, DateTime from, DateTime to, string regimeSymbol = "SPY", CancellationToken ct = default)
    {
        var lookbackFrom = from.AddDays(-400);
        List<OhlcvBar> indexBars;
        try
        {
            indexBars = await dataFeed.GetHistoricalBarsAsync(regimeSymbol, TimeFrame.Daily, lookbackFrom, to, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Symbol} 데이터 조회 실패", regimeSymbol);
            return null;
        }

        if (indexBars.Count < 200)
        {
            _logger.LogWarning("{Symbol} 데이터 부족: {Count}개 (최소 200개 필요), 기본 강세 레짐 적용", regimeSymbol, indexBars.Count);
            var fallbackRegime = new Dictionary<DateOnly, MarketRegime>();
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                fallbackRegime[DateOnly.FromDateTime(d)] = new MarketRegime
                {
                    SpyAbove200Ma = true,
                    SpyPrice = 0,
                    Spy200Ma = 0,
                    RegimeLabel = "강세(기본)",
                    AsOf = d
                };
            }
            return fallbackRegime;
        }

        var indexBarsArray = indexBars.ToArray();
        var indexCloses = IndicatorService.ExtractCloses(indexBarsArray);
        var index200Sma = _indicators.SMA(indexCloses, 200);
        var regimeByDate = new Dictionary<DateOnly, MarketRegime>();

        for (int i = 0; i < indexBarsArray.Length; i++)
        {
            var date = DateOnly.FromDateTime(indexBarsArray[i].Timestamp);
            var aboveMa = index200Sma[i] > 0 && indexBarsArray[i].Close > index200Sma[i];
            regimeByDate[date] = new MarketRegime
            {
                SpyAbove200Ma = aboveMa,
                SpyPrice = indexBarsArray[i].Close,
                Spy200Ma = index200Sma[i],
                RegimeLabel = aboveMa ? "강세" : "약세",
                AsOf = indexBarsArray[i].Timestamp
            };
        }

        return regimeByDate;
    }

    #endregion

    #region Helpers

    private static IReadOnlyCollection<string> CollectReferenceSymbols(IEnumerable<IPatternDetector> detectors)
    {
        return detectors.OfType<RuleBasedDetector>()
            .SelectMany(detector => detector.Strategy.ReferenceSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal List<IPatternDetector> BuildDetectors(
        List<PatternType> patterns, PatternParameterOverrides? overrides,
        List<CustomPatternDefinition>? customPatterns = null)
    {
        List<IPatternDetector> result;

        if (overrides == null)
        {
            result = _detectors.Where(d => patterns.Contains(d.PatternType)).ToList();
        }
        else
        {
            var mergedSettings = PatternOverrideMerger.Merge(_basePatternSettings, overrides);
            var opts = new OptionsSnapshotWrapper<PatternSettings>(mergedSettings);
            var allDetectors = new List<IPatternDetector>
            {
                new GapUpPullbackDetector(_indicators, opts),
                new BreakoutDetector(_indicators, opts),
                new VwapReversionDetector(_indicators, opts),
                new RsiMeanReversionDetector(_indicators, opts),
                new TrendPullbackDetector(_indicators, opts),
                new OrbDetector(_indicators, opts),
                new VolumeSpikeContinuationDetector(_indicators, opts),
                new EarningsDriftDetector(_indicators, opts),
                new IndexRegimeFilterDetector(_indicators, opts),
                new VolatilityExpansionDetector(_indicators, opts),
                new MomentumReversalDetector(_indicators, opts),
                new MultiTimeframeTrendDetector(_indicators, opts),
                new MeanReversionChannelDetector(_indicators, opts),
                new Rsi2BollingerDetector(_indicators, opts),
                new CumulativeRsi2Detector(_indicators, opts),
                new VolatilityBreakoutDetector(_indicators, opts)
            };
            result = allDetectors.Where(d => patterns.Contains(d.PatternType)).ToList();
        }

        // 커스텀 패턴 추가
        if (customPatterns != null && patterns.Contains(PatternType.Custom))
        {
            foreach (var cp in customPatterns)
            {
                result.Add(new RuleBasedDetector(_indicators, cp));
            }
        }

        return result;
    }

    #endregion

    /// <summary>IOptionsSnapshot 래퍼. BacktestService에서 수동 생성한 detector에 전달용.</summary>
    private sealed class OptionsSnapshotWrapper<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public T Value { get; }
        public OptionsSnapshotWrapper(T value) => Value = value;
        public T Get(string? name) => Value;
    }

    #region Parameter Optimization

    /// <summary>
    /// 그리드 서치 방식으로 커스텀 패턴의 파라미터를 최적화합니다.
    /// basePattern의 AtrStopMultiplier, AtrTargetMultiplier, MaxHoldingBars 등의 파라미터 조합을
    /// 순차적으로 백테스트하고 rankBy 기준으로 정렬된 상위 결과를 반환합니다.
    /// </summary>
    public async Task<OptimizeResponse> RunOptimizationAsync(
        OptimizeRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── 파라미터 조합 생성 (2단계 전략) ──
        var allCombinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(request.OptimizeParams);
        var totalCombinations = allCombinations.Count;

        // 2단계 전략: Coarse(60%) → Fine(40%)
        // 전체 조합이 maxCombinations 이하면 전부 실행 (1단계만)
        List<OptimizeParamSnapshot> combinations;
        int stage2Budget = 0;
        if (allCombinations.Count <= request.MaxCombinations)
        {
            combinations = allCombinations;
        }
        else
        {
            // Stage 1: 같은 요청에서 같은 후보가 선택되는 재현 가능한 균등 표본
            var stage1Budget = (int)(request.MaxCombinations * 0.6);
            stage2Budget = request.MaxCombinations - stage1Budget;
            combinations = StrategyOptimizationSpace.SelectDeterministicSample(allCombinations, stage1Budget);
        }

        _logger.LogInformation(
            "파라미터 최적화 시작: 총 {Total}개 조합, Stage1={S1}개, Stage2 예산={S2}개, 심볼={Symbols}",
            totalCombinations, combinations.Count, stage2Budget, string.Join(",", request.Symbols));

        // ── IS/OOS 기간 분할 ──
        var oosPercent = Math.Clamp(request.OosPercent, 0m, 0.5m);
        var totalDays = (request.To - request.From).TotalDays;
        var isTo = oosPercent > 0
            ? request.From.AddDays(totalDays * (double)(1m - oosPercent))
            : request.To;
        var oosFrom = isTo;
        var oosTo = request.To;
        var hasOos = oosPercent > 0 && oosFrom < oosTo;

        // ── 데이터 피드 및 레짐 맵 1회 준비 ──
        var dataFeed = request.DataSource.HasValue
            ? _dataFeedFactory.GetService(request.DataSource.Value)
            : await _dataFeedFactory.GetServiceAsync(ct);

        var regimeSymbol = request.DataSource == Models.Enums.DataSource.LsSecurities ? "069500" : "SPY";
        var regimeByDate = await BuildRegimeMapAsync(dataFeed, request.From, request.To, regimeSymbol, ct);
        if (regimeByDate == null)
        {
            return new OptimizeResponse
            {
                TotalCombinations = totalCombinations,
                TestedCombinations = 0,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        // ── 심볼 데이터 사전 로드 (타임프레임별) ──
        // TimeFrameOptions가 있으면 여러 타임프레임, 없으면 요청의 기본 타임프레임 1개
        var timeFramesToLoad = request.OptimizeParams.TimeFrameOptions is { Count: > 0 }
            ? request.OptimizeParams.TimeFrameOptions.Select(tf => (Models.Enums.TimeFrame)tf).Distinct().ToList()
            : new List<Models.Enums.TimeFrame> { request.TimeFrame };

        var dataByTimeFrame = new Dictionary<Models.Enums.TimeFrame, IReadOnlyDictionary<string, PreparedSymbolData>>();
        var optimizationSymbols = request.Symbols
            .Concat(CollectReferenceSymbols([new RuleBasedDetector(_indicators, request.BasePattern)]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tf in timeFramesToLoad)
        {
            var prepared = await _dataPreparer.PrepareAsync(
                dataFeed, optimizationSymbols, tf, request.From, request.To,
                _basePatternSettings.CumulativeRsi2, _basePatternSettings.Tqqq200Sma, ct);
            if (prepared.HasData)
                dataByTimeFrame[tf] = prepared.Symbols;
        }

        if (dataByTimeFrame.Count == 0)
        {
            _logger.LogWarning("최적화: 유효한 심볼 데이터 없음");
            return new OptimizeResponse
            {
                TotalCombinations = totalCombinations,
                TestedCombinations = 0,
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        // 단일 타임프레임 shortcut (기존 코드 호환)
        var defaultTf = request.TimeFrame;
        var fullDataMap = dataByTimeFrame.ContainsKey(defaultTf)
            ? dataByTimeFrame[defaultTf]
            : dataByTimeFrame.Values.First();

        var riskParams = new BacktestRiskParameters(
            RiskPerTradePercent: _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: _tradingSettings.MaxPositionsPerSector
        );

        // ── 조합별 백테스트 순차 실행 ──
        var resultItems = new List<OptimizeResultItem>(combinations.Count);

        foreach (var combo in combinations)
        {
            ct.ThrowIfCancellationRequested();

            // 패턴 복사 + 파라미터 오버라이드 적용
            var patternCopy = StrategyVariantFactory.ClonePatternDefinition(request.BasePattern);
            StrategyVariantFactory.ApplyOptimizeOverrides(patternCopy, combo);

            var detectors = new List<IPatternDetector>
            {
                new RuleBasedDetector(_indicators, patternCopy)
            };

            try
            {
                // 타임프레임 결정: 조합에 지정되어 있으면 사용, 없으면 요청 기본값
                var comboTf = combo.TimeFrame.HasValue
                    ? (Models.Enums.TimeFrame)combo.TimeFrame.Value
                    : request.TimeFrame;
                var comboDataMap = dataByTimeFrame.TryGetValue(comboTf, out var tfMap) ? tfMap : fullDataMap;

                var btResult = await RunCoreWithPreloadedDataAsync(
                    request.Symbols, comboDataMap, detectors, regimeByDate,
                    request.From, isTo, request.InitialCapital,
                    slippagePercent: 0.05m, commissionPerTrade: 1.00m,
                    comboTf, riskParams,
                    exitOverrides: null, slippageModel: SlippageModel.Adaptive,
                    weightStrategy: null, effectivePatternSettings: _basePatternSettings, ct);

                resultItems.Add(new OptimizeResultItem
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
                _logger.LogWarning(ex, "최적화 조합 백테스트 실패 — 건너뜀");
            }
        }

        // ── Stage 2: 상위 결과 주변 정밀 탐색 ──
        if (stage2Budget > 0 && resultItems.Count >= 3)
        {
            var stage1Top = OptimizationResultRanker.RankOptimizeResults(resultItems, request.RankBy, 5);
            var neighbors = StrategyOptimizationSpace.GenerateNeighborCombinations(stage1Top.Select(r => r.Params).ToList(),
                request.OptimizeParams, stage2Budget, allCombinations);

            _logger.LogInformation("Stage 2 정밀 탐색: {Count}개 이웃 조합 테스트", neighbors.Count);

            foreach (var combo in neighbors)
            {
                ct.ThrowIfCancellationRequested();
                var patternCopy = StrategyVariantFactory.ClonePatternDefinition(request.BasePattern);
                StrategyVariantFactory.ApplyOptimizeOverrides(patternCopy, combo);
                var detectors2 = new List<IPatternDetector> { new RuleBasedDetector(_indicators, patternCopy) };
                try
                {
                    var comboTf = combo.TimeFrame.HasValue
                        ? (Models.Enums.TimeFrame)combo.TimeFrame.Value
                        : request.TimeFrame;
                    var comboDataMap = dataByTimeFrame.TryGetValue(comboTf, out var tfMap2) ? tfMap2 : fullDataMap;
                    var btResult = await RunCoreWithPreloadedDataAsync(
                        request.Symbols, comboDataMap, detectors2, regimeByDate,
                        request.From, isTo, request.InitialCapital,
                        0.05m, 1.00m, comboTf, riskParams,
                        null, SlippageModel.Adaptive, null, _basePatternSettings, ct);
                    resultItems.Add(new OptimizeResultItem
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
                    _logger.LogWarning(ex, "Stage 2 백테스트 실패");
                }
            }
        }

        // ── rankBy 기준 정렬 ──
        var ranked = OptimizationResultRanker.RankOptimizeResults(resultItems, request.RankBy, request.MaxResults);

        // ── OOS 검증: 상위 N개에 대해 OOS 기간 재백테스트 ──
        if (hasOos)
        {
            foreach (var item in ranked)
            {
                ct.ThrowIfCancellationRequested();
                var patternCopy = StrategyVariantFactory.ClonePatternDefinition(request.BasePattern);
                StrategyVariantFactory.ApplyOptimizeOverrides(patternCopy, item.Params);
                var oosDetectors = new List<IPatternDetector> { new RuleBasedDetector(_indicators, patternCopy) };

                var comboTf = item.Params.TimeFrame.HasValue
                    ? (Models.Enums.TimeFrame)item.Params.TimeFrame.Value
                    : request.TimeFrame;
                var comboDataMap = dataByTimeFrame.TryGetValue(comboTf, out var tfMap) ? tfMap : fullDataMap;

                try
                {
                    var oosResult = await RunCoreWithPreloadedDataAsync(
                        request.Symbols, comboDataMap, oosDetectors, regimeByDate,
                        oosFrom, oosTo, request.InitialCapital,
                        0.05m, 1.00m, comboTf, riskParams,
                        null, SlippageModel.Adaptive, null, _basePatternSettings, ct);

                    item.OosTotalReturn  = oosResult.TotalReturnPercent * 100;
                    item.OosSortinoRatio = oosResult.SortinoRatio;
                    item.OosSharpeRatio  = oosResult.SharpeRatio;
                    item.OosMaxDrawdown  = oosResult.MaxDrawdown * 100;
                    item.OosWinRate      = oosResult.OverallWinRate * 100;
                    item.OosTotalTrades      = oosResult.TotalTrades;
                    item.OosProfitFactor     = oosResult.ProfitFactor;
                    item.OosCalmarRatio      = oosResult.CalmarRatio;
                    item.OosAnnualizedReturn = oosResult.AnnualizedReturn;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "OOS 백테스트 실패");
                }
            }
        }

        sw.Stop();
        _logger.LogInformation("파라미터 최적화 완료: {Tested}개 테스트, {ElapsedMs}ms", resultItems.Count, sw.ElapsedMilliseconds);

        return new OptimizeResponse
        {
            TotalCombinations  = totalCombinations,
            TestedCombinations = resultItems.Count,
            ElapsedMs          = sw.ElapsedMilliseconds,
            Results            = ranked,
            IsFrom             = request.From,
            IsTo               = isTo,
            OosFrom            = hasOos ? oosFrom : null,
            OosTo              = hasOos ? oosTo : null,
        };
    }


    #endregion
}

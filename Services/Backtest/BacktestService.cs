using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
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
    private readonly TradingSettings _tradingSettings;
    private readonly PatternSettings _basePatternSettings;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<BacktestService> _logger;

    public BacktestService(
        IDataFeedServiceFactory dataFeedFactory,
        IEnumerable<IPatternDetector> detectors,
        IIndicatorService indicators,
        IOptions<TradingSettings> tradingSettings,
        IOptions<PatternSettings> patternSettings,
        ISettingsRepository settingsRepo,
        ILogger<BacktestService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _detectors = detectors;
        _indicators = indicators;
        _tradingSettings = tradingSettings.Value;
        _basePatternSettings = patternSettings.Value;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    /// <summary>OptimizationJobExecutor가 데이터 로드 시 재사용할 수 있도록 노출</summary>
    internal IIndicatorService Indicators => _indicators;

    /// <summary>최적화 실행 시 기본 리스크 파라미터 (appsettings 기반)</summary>
    internal RiskParams DefaultRiskParams => new(
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

        var riskParams = new RiskParams(
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
            result.MonteCarlo = MonteCarloSimulator.Run(
                result.Trades, request.InitialCapital, request.MonteCarloSimulations);
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
        RiskParams? riskParams = null,
        PatternParameterOverrides? exitOverrides = null,
        SlippageModel slippageModel = SlippageModel.Adaptive,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        riskParams ??= new RiskParams(
            RiskPerTradePercent: _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: _tradingSettings.MaxPositionsPerSector
        );

        var simulator = new TradeSimulator(_indicators, _logger);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;
        effectivePatternSettings ??= ResolvePatternSettings(exitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;

        // ── Phase 1: 모든 심볼 데이터 사전 로드 & 지표 계산 ──
        var symbolDataMap = new Dictionary<string, SymbolPreparedData>();
        var warmupDays = timeFrame switch
        {
            TimeFrame.OneMinute     => 2,
            TimeFrame.FiveMinute    => 10,
            TimeFrame.FifteenMinute => 15,
            _                       => 400
        };

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fetchFrom = from.AddDays(-warmupDays);
                var bars = await dataFeed.GetHistoricalBarsAsync(symbol, timeFrame, fetchFrom, to, ct);

                if (bars.Count < TradeSimulator.MinWarmupBars)
                {
                    string warning = timeFrame is TimeFrame.OneMinute or TimeFrame.FiveMinute or TimeFrame.FifteenMinute
                        ? $"{symbol}: 분봉 데이터 부족 ({bars.Count}개). 시작일을 조정하세요."
                        : $"{symbol}: 데이터 부족 ({bars.Count}개, 최소 {TradeSimulator.MinWarmupBars}개 필요)";
                    warnings.Add(warning);
                    continue;
                }

                var barsArray = bars.ToArray();
                var atrArray = _indicators.ATR(barsArray, 14);
                var closesArray = IndicatorService.ExtractCloses(barsArray);
                var sma200Array = _indicators.SMA(closesArray, 200);
                var cumulativeRsi2Array = _indicators.CumulativeRsi(
                    closesArray, cumulativeRsi2Config.RsiPeriod, cumulativeRsi2Config.CumulativePeriod);
                var cumulativeRsi2TrendMaArray = _indicators.SMA(
                    closesArray, cumulativeRsi2Config.LongTrendMaPeriod);

                var dateToIndex = new Dictionary<DateOnly, int>();
                for (int i = 0; i < barsArray.Length; i++)
                    dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                symbolDataMap[symbol] = new SymbolPreparedData(
                    barsArray, atrArray, closesArray, sma200Array,
                    cumulativeRsi2Array, cumulativeRsi2TrendMaArray, dateToIndex);

                var firstTs = barsArray[0].Timestamp;
                if (!actualDataFrom.HasValue || firstTs < actualDataFrom.Value)
                    actualDataFrom = firstTs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Symbol} 백테스트 데이터 로드 실패", symbol);
                warnings.Add($"{symbol}: 데이터 로드 실패 — {ex.Message}");
            }
        }

        if (symbolDataMap.Count == 0)
        {
            _logger.LogWarning("유효한 심볼 데이터가 없습니다");
            return new BacktestResult { Warnings = warnings };
        }

        return await RunSimulationAsync(
            symbols, symbolDataMap, detectors, regimeByDate,
            from, to, initialCapital, slippagePercent, commissionPerTrade,
            timeFrame, riskParams, exitOverrides, slippageModel,
            warnings, actualDataFrom, simulator, weightStrategy, cumulativeRsi2Config, ct);
    }

    /// <summary>
    /// 핵심 시뮬레이션 루프 (Phase 2~3). RunCoreAsync와 RunCoreWithPreloadedDataAsync가 공유.
    /// symbolDataMap이 이미 구성된 상태에서 호출됩니다.
    /// </summary>
    private async Task<BacktestResult> RunSimulationAsync(
        List<string> symbols,
        Dictionary<string, SymbolPreparedData> symbolDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame,
        RiskParams riskParams,
        PatternParameterOverrides? exitOverrides,
        SlippageModel slippageModel,
        List<string> warnings,
        DateTime? actualDataFrom,
        TradeSimulator simulator,
        WeightStrategy? weightStrategy,
        CumulativeRsi2Config cumulativeRsi2Config,
        CancellationToken ct)
    {
        // ── Phase 2: 날짜순 포트폴리오 시뮬레이션 ──
        var allDates = symbolDataMap.Values
            .SelectMany(d => d.DateToIndex.Keys)
            .Distinct()
            .Where(d => d >= DateOnly.FromDateTime(from))
            .OrderBy(d => d)
            .ToList();

        var openPositions = new Dictionary<string, TradeSimulator.OpenPosition>();
        var trades = new List<TradeRecord>();
        var pepCache = new Dictionary<PatternType, TradeSimulator.PatternExitProfile>();
        var maxTotalPositions = riskParams.MaxTotalPositions;
        var riskPerTrade = riskParams.RiskPerTradePercent;
        var dailyLossLimitPercent = riskParams.DailyLossLimitPercent;
        // currentEquity: 실현된 거래 PnL 누적 → 복리 포지션 사이징에 사용
        // 미실현 포지션 가치 제외 (보수적 접근)
        var currentEquity = initialCapital;
        var dailyStartEquity = initialCapital;
        var dailyLossDate = DateOnly.MinValue;
        var weightReducedCount = 0;
        // [BUG-PB-08] Kelly 사이징용 (결과 생성부에서 실제 계산, 루프 중에는 0 = FixedRisk)
        decimal kellyFraction = 0, halfKellyFraction = 0;
        // [A-1] NextOpen 진입 대기 시그널: (symbol → pending signal 정보)
        var pendingNextOpenSignals = new Dictionary<string, (decimal entryPrice, decimal stopLoss, decimal target, decimal stopDistance, decimal entryAtr, long entryVolume, decimal equityAtEntry, TradeSimulator.PatternExitProfile? customExit, decimal riskPerTradeSnap, decimal effectiveMaxPosSnap)>();
        var maxWindow = timeFrame switch
        {
            TimeFrame.OneMinute     => 800,
            TimeFrame.FiveMinute    => 800,
            TimeFrame.FifteenMinute => 600,
            _                       => 260
        };

        // ── 커스텀 패턴 고급 기능: 상태 추적 ──
        // 서킷브레이커, 재진입 쿨다운, 스케일링 등에 사용
        var customDetector = detectors.OfType<RuleBasedDetector>().FirstOrDefault();
        CircuitBreakerConfig? circuitBreaker = null;
        ReentryConfig? reentryConfig = null;
        PortfolioRulesConfig? portfolioRules = null;
        if (customDetector != null)
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var def = customDetector.Definition;
            circuitBreaker = JsonSerializer.Deserialize<CircuitBreakerConfig>(def.CircuitBreakerJson, jsonOpts) ?? new();
            reentryConfig = JsonSerializer.Deserialize<ReentryConfig>(def.ReentryJson, jsonOpts) ?? new();
            portfolioRules = JsonSerializer.Deserialize<PortfolioRulesConfig>(def.PortfolioRulesJson, jsonOpts) ?? new();
        }

        var consecutiveLosses = 0;
        var circuitBreakerUntilDate = DateOnly.MinValue; // 서킷브레이커 해제 날짜
        var peakSimEquity = initialCapital;
        var circuitBreakerTripped = false; // 최대 낙폭 서킷브레이커 (영구)
        // 종목별 재진입 쿨다운: (symbol → 쿨다운 해제 날짜)
        var reentryCooldowns = new Dictionary<string, DateOnly>();
        // 하루 진입 수 추적
        var dailyEntryCount = 0;
        var lastEntryDate = DateOnly.MinValue;
        // 스케일링 횟수 추적: (symbol → rule index → count)
        var positionScaleCounts = new Dictionary<string, Dictionary<int, int>>();

        // ── 참조 종목 데이터 준비 (RefSymbol 지원) ──
        if (customDetector != null)
        {
            var refData = new Dictionary<string, OhlcvBar[]>();
            foreach (var (sym, sd) in symbolDataMap)
                refData[sym.ToUpperInvariant()] = sd.Bars;
            customDetector.SetReferenceData(refData);
        }

        foreach (var date in allDates)
        {
            ct.ThrowIfCancellationRequested();
            var regime = TradeSimulator.GetRegimeForDate(date, regimeByDate);

            if (date != dailyLossDate)
            {
                dailyLossDate = date;
                dailyStartEquity = currentEquity;
            }

            // 하루 진입 수 리셋
            if (date != lastEntryDate) dailyEntryCount = 0;

            // ── 2a. 보유 중인 모든 포지션의 청산 로직 ──
            foreach (var symbol in openPositions.Keys.ToList())
            {
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.DateToIndex.TryGetValue(date, out var barIdx)) continue;

                var pos = openPositions[symbol];
                var tradesBefore = trades.Count;

                // ── 규칙 기반 청산 체크 (ATR 청산 전에 먼저 평가) ──
                if (customDetector != null && customDetector.HasExitRules)
                {
                    var windowSize2 = Math.Min(barIdx + 1, maxWindow);
                    var windowStart2 = barIdx + 1 - windowSize2;
                    var windowBars2 = sd.Bars[windowStart2..(barIdx + 1)];

                    if (customDetector.ShouldExit(windowBars2))
                    {
                        var exitPrice = sd.Bars[barIdx].Close;
                        trades.Add(TradeSimulator.CreateTradeRecord(
                            symbol, pos, exitPrice, sd.Bars[barIdx].Timestamp,
                            "규칙 청산", pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity));
                        for (int ti = tradesBefore; ti < trades.Count; ti++)
                            currentEquity += trades[ti].PnL;
                        openPositions.Remove(symbol);
                        positionScaleCounts.Remove(symbol);
                        // 재진입 쿨다운 등록
                        RegisterCooldown(symbol, date, trades[^1], reentryConfig, reentryCooldowns);
                        UpdateCircuitBreaker(trades[^1], ref consecutiveLosses, ref circuitBreakerUntilDate,
                            date, circuitBreaker);
                        continue;
                    }
                }

                // ── 스케일링 체크 ──
                if (customDetector != null && customDetector.HasScalingRules)
                {
                    var windowSize3 = Math.Min(barIdx + 1, maxWindow);
                    var windowStart3 = barIdx + 1 - windowSize3;
                    var windowBars3 = sd.Bars[windowStart3..(barIdx + 1)];
                    var currentProfitPct = pos.EntryPrice > 0
                        ? (sd.Bars[barIdx].Close - pos.EntryPrice) / pos.EntryPrice * 100
                        : 0;
                    if (!positionScaleCounts.TryGetValue(symbol, out var sc))
                    {
                        sc = new Dictionary<int, int>();
                        positionScaleCounts[symbol] = sc;
                    }
                    var matchedScale = customDetector.CheckScaling(windowBars3, currentProfitPct, sc);
                    if (matchedScale != null)
                    {
                        var baseQty = pos.Quantity;
                        var scaleQty = Math.Max(1, (int)(baseQty * matchedScale.Percent / 100m));

                        if (matchedScale.Direction == "SCALE_IN")
                        {
                            var newQty = (pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity) + scaleQty;
                            pos.CurrentQuantity = newQty;
                            pos.TotalCost += sd.Bars[barIdx].Close * scaleQty;
                        }
                        else // SCALE_OUT
                        {
                            var curQty = pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity;
                            var sellQty = Math.Min(scaleQty, curQty - 1);
                            if (sellQty > 0)
                            {
                                trades.Add(TradeSimulator.CreateTradeRecord(
                                    symbol, pos, sd.Bars[barIdx].Close,
                                    sd.Bars[barIdx].Timestamp, $"스케일아웃({matchedScale.Percent}%)", sellQty));
                                pos.CurrentQuantity = curQty - sellQty;
                                for (int ti = tradesBefore; ti < trades.Count; ti++)
                                    currentEquity += trades[ti].PnL;
                            }
                        }
                    }
                }

                // ── 기존 ATR 기반 청산 로직 ──
                tradesBefore = trades.Count;
                var exitResult = simulator.ProcessExitLogic(
                    pos, sd.Bars[barIdx], barIdx,
                    sd.Atr[barIdx], sd.Sma200[barIdx],
                    sd.CumulativeRsi2[barIdx], sd.CumulativeRsi2TrendMa[barIdx], cumulativeRsi2Config,
                    pepCache, exitOverrides, symbol, trades);

                if (exitResult == null)
                {
                    for (int ti = tradesBefore; ti < trades.Count; ti++)
                        currentEquity += trades[ti].PnL;
                    openPositions.Remove(symbol);
                    positionScaleCounts.Remove(symbol);
                    // 재진입 쿨다운 등록
                    if (trades.Count > tradesBefore)
                    {
                        RegisterCooldown(symbol, date, trades[^1], reentryConfig, reentryCooldowns);
                        UpdateCircuitBreaker(trades[^1], ref consecutiveLosses, ref circuitBreakerUntilDate,
                            date, circuitBreaker);
                    }
                }
                else
                    openPositions[symbol] = exitResult;
            }

            // ── 피크 에퀴티 + 최대낙폭 서킷브레이커 체크 ──
            if (currentEquity > peakSimEquity) peakSimEquity = currentEquity;
            if (circuitBreaker != null && circuitBreaker.MaxDrawdownPercent > 0 && peakSimEquity > 0)
            {
                var dd = (peakSimEquity - currentEquity) / peakSimEquity * 100;
                if (dd >= circuitBreaker.MaxDrawdownPercent)
                    circuitBreakerTripped = true;
            }

            var dailyLossLimitReached =
                dailyLossLimitPercent > 0 &&
                dailyStartEquity > 0 &&
                currentEquity <= dailyStartEquity * (1 - dailyLossLimitPercent);

            // ── [A-1] NextOpen 대기 시그널 처리 ──
            // 이전 봉에서 대기 등록된 시그널을 이번 봉의 Open 가격으로 진입
            if (dailyLossLimitReached)
            {
                pendingNextOpenSignals.Clear();
            }
            else
            {
                foreach (var pendingSymbol in pendingNextOpenSignals.Keys.ToList())
                {
                    if (openPositions.ContainsKey(pendingSymbol)) { pendingNextOpenSignals.Remove(pendingSymbol); continue; }
                    if (!symbolDataMap.TryGetValue(pendingSymbol, out var pendingSd)) { pendingNextOpenSignals.Remove(pendingSymbol); continue; }
                    if (!pendingSd.DateToIndex.TryGetValue(date, out var pendingBarIdx)) { pendingNextOpenSignals.Remove(pendingSymbol); continue; }

                    var pending = pendingNextOpenSignals[pendingSymbol];
                    var nextOpen = pendingSd.Bars[pendingBarIdx].Open;
                    if (nextOpen <= 0) { pendingNextOpenSignals.Remove(pendingSymbol); continue; }

                    // NextOpen 기준으로 StopLoss/Target 재계산
                    var newStopDistance = pending.stopDistance; // ATR 기반 거리 유지
                    var newStop   = nextOpen - newStopDistance;
                    var rMultiple = pending.entryPrice > 0 && pending.stopLoss < pending.entryPrice
                        ? (pending.target - pending.entryPrice) / (pending.entryPrice - pending.stopLoss)
                        : 2.0m;
                    var newTarget = nextOpen + newStopDistance * rMultiple;

                    var pendingRiskAmount = pending.equityAtEntry * pending.riskPerTradeSnap;
                    var pendingQty = (int)(pendingRiskAmount / newStopDistance);
                    if (pendingQty <= 0) pendingQty = 1;
                    var pendingMaxQty = pending.effectiveMaxPosSnap > 0
                        ? (int)(pending.equityAtEntry * pending.effectiveMaxPosSnap / nextOpen)
                        : 0;
                    if (pendingMaxQty > 0) pendingQty = Math.Min(pendingQty, pendingMaxQty);

                    openPositions[pendingSymbol] = new TradeSimulator.OpenPosition
                    {
                        PatternType           = PatternType.Custom,
                        EntryPrice            = nextOpen,
                        OriginalStop          = newStop,
                        StopLoss              = newStop,
                        Target                = newTarget,
                        Quantity              = pendingQty,
                        CurrentQuantity       = pendingQty,
                        TotalCost             = nextOpen * pendingQty,
                        EntryTime             = pendingSd.Bars[pendingBarIdx].Timestamp,
                        EntryBarIndex         = pendingBarIdx,
                        EntryAtr              = pending.entryAtr,
                        EntryVolume           = pending.entryVolume,
                        HighestHighSinceEntry = pendingSd.Bars[pendingBarIdx].High,
                        LowestLowSinceEntry   = pendingSd.Bars[pendingBarIdx].Low,
                        RiskDistance          = newStopDistance,
                        EquityAtEntry         = pending.equityAtEntry,
                        CustomExitProfile     = pending.customExit
                    };

                    pendingNextOpenSignals.Remove(pendingSymbol);
                    dailyEntryCount++;
                    lastEntryDate = date;
                }
            }

            // ── 2b. 새 진입 ──
            // 서킷브레이커 체크
            if (circuitBreakerTripped) continue;
            if (circuitBreaker != null && circuitBreaker.ConsecutiveLossLimit > 0 && date < circuitBreakerUntilDate)
                continue;
            if (dailyLossLimitReached) continue;

            // 포트폴리오 규칙: 최대 포지션 수
            var effectiveMaxPos = maxTotalPositions;
            if (portfolioRules != null && portfolioRules.MaxTotalPositions > 0)
                effectiveMaxPos = Math.Min(effectiveMaxPos, portfolioRules.MaxTotalPositions);
            if (openPositions.Count >= effectiveMaxPos) continue;

            // 하루 최대 진입 수
            if (portfolioRules != null && portfolioRules.MaxEntriesPerDay > 0 && dailyEntryCount >= portfolioRules.MaxEntriesPerDay)
                continue;

            foreach (var symbol in symbols)
            {
                if (openPositions.ContainsKey(symbol)) continue;
                if (openPositions.Count >= effectiveMaxPos) break;
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.DateToIndex.TryGetValue(date, out var barIdx)) continue;
                if (barIdx < TradeSimulator.MinWarmupBars) continue;

                // 재진입 쿨다운 체크
                if (reentryCooldowns.TryGetValue(symbol, out var cooldownUntil) && date < cooldownUntil)
                    continue;

                var windowSize = Math.Min(barIdx + 1, maxWindow);
                var windowStart = barIdx + 1 - windowSize;
                var windowBars = sd.Bars[windowStart..(barIdx + 1)];

                foreach (var detector in detectors)
                {
                    try
                    {
                        var signal = await detector.DetectAsync(symbol, windowBars, regime!, ct);
                        if (signal == null) continue;
                        if (signal.EntryPrice <= 0 || signal.StopLossPrice <= 0) continue;

                        var stopDistance = Math.Abs(signal.EntryPrice - signal.StopLossPrice);
                        if (stopDistance <= 0) continue;

                        var effectiveEquity = Math.Max(currentEquity, initialCapital * 0.10m);

                        // ── 비중 전략 적용 ──
                        if (weightStrategy != null && regime != null)
                        {
                            var wScale = GetWeightScale(regime, weightStrategy);
                            effectiveEquity *= wScale;
                            if (wScale < 1.0m) weightReducedCount++;
                        }

                        // ── 커스텀 패턴 비중 단계 적용 ──
                        if (signal.AllocationScale != 1.0m && signal.AllocationScale > 0)
                        {
                            effectiveEquity *= signal.AllocationScale;
                            if (signal.AllocationScale < 1.0m) weightReducedCount++;
                        }

                        // [E-2] 포트폴리오 상관관계 필터
                        if (portfolioRules != null && portfolioRules.MaxCorrelation > 0
                            && openPositions.Count > 0)
                        {
                            bool correlationBlocked = false;
                            foreach (var existingSymbol in openPositions.Keys)
                            {
                                if (!symbolDataMap.TryGetValue(existingSymbol, out var existingSd)) continue;
                                if (!symbolDataMap.TryGetValue(symbol, out var newSd)) continue;

                                // 최근 60개 수익률로 Pearson 상관계수 계산
                                const int corrWindow = 60;
                                var corrCorr = ComputePearsonCorrelation(
                                    existingSd.Closes, existingSd.DateToIndex,
                                    newSd.Closes, newSd.DateToIndex,
                                    date, corrWindow);

                                if (corrCorr > (double)portfolioRules.MaxCorrelation)
                                {
                                    correlationBlocked = true;
                                    break;
                                }
                            }
                            if (correlationBlocked) continue;
                        }

                        // [BUG-PB-08] SizingMode Kelly/HalfKelly 반영
                        var effectiveRisk = riskPerTrade;
                        if (detector is RuleBasedDetector rbdSizing)
                        {
                            var sizingMode = rbdSizing.Definition.SizingMode;
                            if (sizingMode == "Kelly" && kellyFraction > 0)
                                effectiveRisk = kellyFraction;
                            else if (sizingMode == "HalfKelly" && halfKellyFraction > 0)
                                effectiveRisk = halfKellyFraction;
                        }
                        var riskAmount = effectiveEquity * effectiveRisk;
                        var quantity = (int)(riskAmount / stopDistance);
                        if (quantity <= 0) quantity = 1;

                        var capRatio = effectiveMaxPos > 0 ? 1.0m / effectiveMaxPos : 0.10m;
                        // 포트폴리오 규칙: 단일 종목 최대 비율
                        if (portfolioRules != null && portfolioRules.MaxSinglePositionPercent > 0)
                            capRatio = Math.Min(capRatio, portfolioRules.MaxSinglePositionPercent / 100m);
                        var maxQty = (int)(effectiveEquity * capRatio / signal.EntryPrice);
                        if (maxQty > 0) quantity = Math.Min(quantity, maxQty);

                        var entryAtr = sd.Atr[barIdx] > 0 ? sd.Atr[barIdx] : stopDistance;

                        TradeSimulator.PatternExitProfile? customExit = null;
                        if (detector is RuleBasedDetector rbd)
                        {
                            var def = rbd.Definition;
                            customExit = new TradeSimulator.PatternExitProfile(
                                MaxHoldingBars: def.MaxHoldingBars,
                                EnableTrailingStop: def.TrailingAtr > 0,
                                TrailingStopAtrMultiplier: def.TrailingAtr,
                                TrailingActivationR: 1.0m,
                                EnablePartialProfit: def.PartialProfitR > 0,
                                PartialProfitRMultiple: def.PartialProfitR,
                                EnableTargetExit: true,
                                EnableTimeExit: true
                            );
                        }

                        // [A-1] NextOpen 진입 모드 체크
                        bool isNextOpen = detector is RuleBasedDetector rbdEntry
                            && rbdEntry.Definition.EntryMode == "NextOpen";

                        if (isNextOpen && !pendingNextOpenSignals.ContainsKey(symbol))
                        {
                            // 이번 봉에서 시그널만 저장하고, 다음 봉 Open에서 진입
                            signal.PendingEntry = true;
                            var capRatioSnap = effectiveMaxPos > 0 ? 1.0m / effectiveMaxPos : 0.10m;
                            pendingNextOpenSignals[symbol] = (
                                signal.EntryPrice,
                                signal.StopLossPrice,
                                signal.TargetPrice,
                                stopDistance,
                                entryAtr,
                                sd.Bars[barIdx].Volume,
                                effectiveEquity,
                                customExit,
                                riskPerTrade,
                                capRatioSnap
                            );
                        }
                        else if (!isNextOpen)
                        {
                            openPositions[symbol] = new TradeSimulator.OpenPosition
                            {
                                PatternType           = detector.PatternType,
                                EntryPrice            = signal.EntryPrice,
                                OriginalStop          = signal.StopLossPrice,
                                StopLoss              = signal.StopLossPrice,
                                Target                = signal.TargetPrice,
                                Quantity              = quantity,
                                CurrentQuantity       = quantity,
                                TotalCost             = signal.EntryPrice * quantity,
                                EntryTime             = sd.Bars[barIdx].Timestamp,
                                EntryBarIndex         = barIdx,
                                EntryAtr              = entryAtr,
                                EntryVolume           = sd.Bars[barIdx].Volume,
                                HighestHighSinceEntry = sd.Bars[barIdx].High,
                                LowestLowSinceEntry   = sd.Bars[barIdx].Low,
                                RiskDistance          = stopDistance,
                                EquityAtEntry         = effectiveEquity,
                                CustomExitProfile     = customExit
                            };

                            dailyEntryCount++;
                            lastEntryDate = date;
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{Symbol} 패턴 {Pattern} 감지 실패", symbol, detector.PatternType);
                    }
                }
            }
        }

        // ── 잔여 포지션 종가 청산 ──
        foreach (var (symbol, pos) in openPositions)
        {
            if (symbolDataMap.TryGetValue(symbol, out var sd) && sd.Bars.Length > 0)
            {
                var lastBar = sd.Bars[^1];
                var exitQty = pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity;
                trades.Add(TradeSimulator.CreateTradeRecord(
                    symbol, pos, lastBar.Close, lastBar.Timestamp, "기간 종료", exitQty));
            }
        }

        trades = trades.OrderBy(t => t.EntryTime).ToList();

        // ── Phase 3: Equity curve & drawdown (슬리피지 적용) ──
        var equity = initialCapital;
        var peakEquity = equity;
        var maxDrawdown = 0m;
        var totalSlippage = 0m;
        var totalCommission = 0m;
        var equityCurve = new List<EquityPoint> { new(from, initialCapital) };

        foreach (var trade in trades)
        {
            decimal slippageCost;
            if (slippageModel == SlippageModel.Adaptive && trade.EntryAtr > 0 && trade.EntryPrice > 0)
            {
                var atrPct = trade.EntryAtr / trade.EntryPrice;
                var volatilityFactor = Math.Max(0.5m, Math.Min(3.0m, atrPct / 0.02m));

                // [F-2] 시장 충격 모델 개선: sqrt(orderRatio) — Almgren-Chriss 계열 표준
                var liquidityFactor = 1.0m;
                if (trade.EntryVolume > 0)
                {
                    var orderRatio = (decimal)trade.Quantity / trade.EntryVolume;
                    // sqrt 모델: 소규모 주문일수록 영향 감소 (선형보다 현실적)
                    var sqrtImpact = (decimal)Math.Sqrt((double)Math.Max(0m, orderRatio));
                    liquidityFactor = Math.Max(0.5m, Math.Min(3.0m, 1.0m + sqrtImpact * 2.0m));
                }

                var adaptiveSlippagePct = slippagePercent / 100m * volatilityFactor * liquidityFactor;
                slippageCost = (trade.EntryPrice + trade.ExitPrice) * adaptiveSlippagePct * trade.Quantity;
            }
            else
            {
                slippageCost = (trade.EntryPrice + trade.ExitPrice) * (slippagePercent / 100m) * trade.Quantity;
            }
            var tradePnl = trade.PnL - slippageCost - commissionPerTrade;

            trade.PnL = tradePnl;
            trade.PnLPercent = trade.EntryPrice > 0
                ? tradePnl / (trade.EntryPrice * trade.Quantity)
                : 0;

            totalSlippage += slippageCost;
            totalCommission += commissionPerTrade;

            equity += tradePnl;
            if (equity > peakEquity) peakEquity = equity;
            var drawdown = peakEquity > 0 ? (peakEquity - equity) / peakEquity : 0;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;

            equityCurve.Add(new EquityPoint(trade.ExitTime, equity));
        }

        var totalReturn = equity - initialCapital;
        var totalReturnPct = initialCapital > 0 ? totalReturn / initialCapital : 0;
        var winCount = trades.Count(t => t.IsWin);
        var overallWinRate = trades.Count > 0 ? (decimal)winCount / trades.Count : 0;

        // ── [B-1] 고급 성과 지표 ──
        var tradingDaysCount = trades.Count >= 2
            ? Math.Max(1, (int)(trades.Max(t => t.ExitTime) - trades.Min(t => t.EntryTime)).TotalDays)
            : 1;
        var annualizedReturn = PerformanceCalculator.ComputeAnnualizedReturn(
            totalReturnPct * 100, tradingDaysCount);
        var sortinoRatio  = PerformanceCalculator.ComputeSortinoRatio(trades, timeFrame);
        var calmarRatio   = PerformanceCalculator.ComputeCalmarRatio(annualizedReturn, maxDrawdown * 100);
        var profitFactor  = PerformanceCalculator.ComputeProfitFactor(trades);

        // ── [E-1] Kelly Criterion ──
        var perPatternStats = PerformanceCalculator.ComputePerPatternStats(trades);
        kellyFraction = 0; halfKellyFraction = 0;
        if (trades.Count > 0)
        {
            var allWins   = trades.Where(t => t.PnL > 0).ToList();
            var allLosses = trades.Where(t => t.PnL < 0).ToList();
            var avgWinPct  = allWins.Count  > 0 ? allWins.Average(t  => t.PnLPercent * 100) : 0;
            var avgLossPct = allLosses.Count > 0 ? Math.Abs(allLosses.Average(t => t.PnLPercent * 100)) : 0;
            kellyFraction     = PerformanceCalculator.ComputeKellyFraction(overallWinRate, avgWinPct, avgLossPct);
            halfKellyFraction = kellyFraction / 2;
        }

        // ── [B-3] MAE/MFE 통계 ──
        var (avgMae, avgMfe, medianMae, medianMfe) = PerformanceCalculator.ComputeMaeMfe(trades);

        // ── [F-1] 레짐별 성과 분해 ──
        // regimeByDate를 DateTime → bool 딕셔너리로 변환
        var spyAbove200Ma = regimeByDate.ToDictionary(
            kv => kv.Key.ToDateTime(TimeOnly.MinValue),
            kv => kv.Value.SpyAbove200Ma);
        var perRegimeStats = PerformanceCalculator.ComputeRegimeStats(trades, spyAbove200Ma);

        // ── [A-2] 생존자 편향 경고 ──
        string? survivorshipWarning = null;
        var highPerformanceEtfs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "TQQQ", "SOXL", "UPRO", "TECL", "FNGU", "SPXL", "QLD", "UDOW" };
        var dateRangeYears = (to - from).TotalDays / 365.0;
        if (symbols.Count <= 5 && dateRangeYears >= 3
            && symbols.Any(s => highPerformanceEtfs.Contains(s)))
        {
            survivorshipWarning = "생존자 편향 주의: 고레버리지/고성과 ETF(TQQQ 등)만으로 장기 백테스트 시 " +
                "결과가 과대 추정될 수 있습니다. 다양한 종목으로 검증하세요.";
        }

        // ── [G-1] 백테스트→라이브 권장 파라미터 (warnings에 정보성 메시지 추가) ──
        if (kellyFraction > 0 && trades.Count >= 10)
        {
            var recommendedSize = Math.Round(halfKellyFraction * 100, 1);
            var winRatePct      = Math.Round(overallWinRate * 100, 1);
            warnings.Add($"[권장 파라미터] Half-Kelly 포지션 크기: {recommendedSize}% | " +
                         $"WinRate {winRatePct}% | ProfitFactor {profitFactor:F2} | Sortino {sortinoRatio:F2}");
        }

        var result = new BacktestResult
        {
            Trades              = trades,
            TotalReturn         = totalReturn,
            TotalReturnPercent  = totalReturnPct,
            MaxDrawdown         = maxDrawdown,
            SharpeRatio         = PerformanceCalculator.ComputeSharpeRatio(trades, timeFrame),
            TotalTrades         = trades.Count,
            OverallWinRate      = overallWinRate,
            PerPatternStats     = perPatternStats,
            PerSymbolStats      = PerformanceCalculator.ComputePerSymbolStats(trades, initialCapital),
            EquityCurve         = equityCurve,
            TotalSlippageCost   = totalSlippage,
            TotalCommissionCost = totalCommission,
            WeightStrategyApplied = weightStrategy != null,
            WeightReducedTrades   = weightReducedCount,
            Warnings            = warnings,
            ActualDataFrom      = actualDataFrom,
            // [B-1] 고급 지표
            SortinoRatio        = sortinoRatio,
            CalmarRatio         = calmarRatio,
            ProfitFactor        = profitFactor,
            AnnualizedReturn    = annualizedReturn,
            // [E-1] Kelly
            KellyFraction       = kellyFraction,
            HalfKellyFraction   = halfKellyFraction,
            // [A-2] 생존자 편향
            SurvivorshipBiasWarning = survivorshipWarning,
            // [F-1] 레짐 분해
            PerRegimeStats      = perRegimeStats,
            // [B-3] MAE/MFE
            AvgMaePercent       = avgMae,
            AvgMfePercent       = avgMfe,
            MedianMaePercent    = medianMae,
            MedianMfePercent    = medianMfe
        };

        return result;
    }

    /// <summary>재진입 쿨다운 등록</summary>
    private static void RegisterCooldown(string symbol, DateOnly date, TradeRecord lastTrade,
        ReentryConfig? config, Dictionary<string, DateOnly> cooldowns)
    {
        if (config == null) return;
        var isLoss = lastTrade.PnL < 0;
        var bars = isLoss ? config.CooldownBarsAfterLoss : config.CooldownBarsAfterWin;
        if (bars > 0)
            cooldowns[symbol] = date.AddDays(bars); // 근사치 (거래일 ≈ 캘린더일)
    }

    /// <summary>서킷브레이커 상태 업데이트</summary>
    private static void UpdateCircuitBreaker(TradeRecord trade, ref int consecutiveLosses,
        ref DateOnly circuitBreakerUntilDate, DateOnly currentDate, CircuitBreakerConfig? config)
    {
        if (config == null || config.ConsecutiveLossLimit <= 0) return;
        if (trade.PnL < 0)
        {
            consecutiveLosses++;
            if (consecutiveLosses >= config.ConsecutiveLossLimit)
            {
                circuitBreakerUntilDate = currentDate.AddDays(config.CooldownBars);
                consecutiveLosses = 0; // 리셋
            }
        }
        else
        {
            consecutiveLosses = 0;
        }
    }

    /// <summary>
    /// 시장 레짐에 따른 비중 스케일링 계수 계산.
    /// SPY vs SMA 위치에 따라 포지션 크기를 조절합니다.
    /// </summary>
    private static decimal GetWeightScale(MarketRegime regime, WeightStrategy ws)
    {
        if (regime.Spy200Ma <= 0) return 1.0m;

        var ratio = regime.SpyPrice / regime.Spy200Ma;

        // 약세장 (지수 < SMA)
        if (!regime.SpyAbove200Ma)
            return ws.BearWeight;

        // 과열2단계 (지수가 SMA 대비 OverheatStage2Pct 이상)
        if (ratio >= ws.OverheatStage2Pct)
            return ws.Overheat2Weight;

        // 과열1단계
        if (ratio >= ws.OverheatStage1Pct)
            return ws.Overheat1Weight;

        // 정상 강세장
        return ws.BullWeight;
    }

    /// <summary>
    /// Walk-Forward 전용 오버로드: 이미 로드된 symbolDataMap에서 날짜 범위를 슬라이싱하여
    /// API 호출 없이 시뮬레이션을 실행합니다.
    /// </summary>
    internal async Task<BacktestResult> RunCoreWithPreloadedDataAsync(
        List<string> symbols,
        Dictionary<string, SymbolPreparedData> fullDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame,
        RiskParams riskParams,
        PatternParameterOverrides? exitOverrides,
        SlippageModel slippageModel,
        WeightStrategy? weightStrategy = null,
        PatternSettings? effectivePatternSettings = null,
        CancellationToken ct = default)
    {
        var simulator = new TradeSimulator(_indicators, _logger);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;
        effectivePatternSettings ??= ResolvePatternSettings(exitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;

        // 사전 로드된 데이터에서 날짜 범위 슬라이싱 (API 재호출 없음)
        var symbolDataMap = new Dictionary<string, SymbolPreparedData>();
        var toDate = DateOnly.FromDateTime(to);

        // warmupDays만큼 앞의 데이터가 필요하므로 from 이전 데이터도 포함
        var warmupDays = timeFrame switch
        {
            TimeFrame.OneMinute     => 2,
            TimeFrame.FiveMinute    => 10,
            TimeFrame.FifteenMinute => 15,
            _                       => 400
        };
        var fetchFrom = DateOnly.FromDateTime(from.AddDays(-warmupDays));

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            if (!fullDataMap.TryGetValue(symbol, out var full)) continue;

            // 날짜 범위에 해당하는 bar 인덱스 범위 결정
            int startIdx = -1, endIdx = -1;
            for (int i = 0; i < full.Bars.Length; i++)
            {
                var d = DateOnly.FromDateTime(full.Bars[i].Timestamp);
                if (d >= fetchFrom && startIdx == -1) startIdx = i;
                if (d <= toDate) endIdx = i;
            }

            if (startIdx == -1 || endIdx < startIdx) continue;

            var barsSlice = full.Bars[startIdx..(endIdx + 1)];
            var atrSlice  = full.Atr[startIdx..(endIdx + 1)];
            var closesSlice = full.Closes[startIdx..(endIdx + 1)];
            var sma200Slice = full.Sma200[startIdx..(endIdx + 1)];
            var cumulativeRsi2Slice = _indicators.CumulativeRsi(
                closesSlice, cumulativeRsi2Config.RsiPeriod, cumulativeRsi2Config.CumulativePeriod);
            var cumulativeRsi2TrendMaSlice = _indicators.SMA(
                closesSlice, cumulativeRsi2Config.LongTrendMaPeriod);

            if (barsSlice.Length < TradeSimulator.MinWarmupBars)
            {
                warnings.Add($"{symbol}: 데이터 부족 ({barsSlice.Length}개)");
                continue;
            }

            // 슬라이싱된 범위에 맞는 dateToIndex 재구성
            var dateToIndex = new Dictionary<DateOnly, int>(barsSlice.Length);
            for (int i = 0; i < barsSlice.Length; i++)
                dateToIndex[DateOnly.FromDateTime(barsSlice[i].Timestamp)] = i;

            symbolDataMap[symbol] = new SymbolPreparedData(
                barsSlice, atrSlice, closesSlice, sma200Slice,
                cumulativeRsi2Slice, cumulativeRsi2TrendMaSlice, dateToIndex);

            var firstTs = barsSlice[0].Timestamp;
            if (!actualDataFrom.HasValue || firstTs < actualDataFrom.Value)
                actualDataFrom = firstTs;
        }

        if (symbolDataMap.Count == 0)
            return new BacktestResult { Warnings = warnings };

        // 이하 RunCoreAsync와 동일한 시뮬레이션 로직 (공통 메서드로 위임)
        return await RunSimulationAsync(
            symbols, symbolDataMap, detectors, regimeByDate,
            from, to, initialCapital, slippagePercent, commissionPerTrade,
            timeFrame, riskParams, exitOverrides, slippageModel,
            warnings, actualDataFrom, simulator, weightStrategy, cumulativeRsi2Config, ct);
    }

    /// <summary>심볼별 사전 계산 데이터</summary>
    internal sealed record SymbolPreparedData(
        OhlcvBar[] Bars,
        decimal[] Atr,
        decimal[] Closes,
        decimal[] Sma200,
        decimal[] CumulativeRsi2,
        decimal[] CumulativeRsi2TrendMa,
        Dictionary<DateOnly, int> DateToIndex);

    #region Walk-Forward Analysis

    private async Task<WalkForwardResult> RunWalkForwardAsync(
        BacktestRequest request,
        IDataFeedService dataFeed,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        RiskParams riskParams,
        CancellationToken ct)
    {
        _logger.LogInformation("Walk-Forward 분석 시작 (IS:{IS}개월, OOS:{OOS}개월)",
            request.WalkForwardInSampleMonths, request.WalkForwardOutOfSampleMonths);
        var effectivePatternSettings = ResolvePatternSettings(request.ParameterOverrides);

        // ── 전체 기간 데이터 1회 사전 로드 (윈도우마다 API 재호출 방지) ──
        // 일봉 기준 warmup 400일치를 포함하여 충분히 이전 데이터부터 로드
        var warmupDays = request.TimeFrame switch
        {
            TimeFrame.OneMinute     => 2,
            TimeFrame.FiveMinute    => 10,
            TimeFrame.FifteenMinute => 15,
            _                       => 400
        };
        var wfFullDataMap = new Dictionary<string, SymbolPreparedData>();
        foreach (var symbol in request.Symbols)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fetchFrom = request.From.AddDays(-warmupDays);
                var bars = await dataFeed.GetHistoricalBarsAsync(symbol, request.TimeFrame, fetchFrom, request.To, ct);
                if (bars.Count < TradeSimulator.MinWarmupBars) continue;

                var barsArray = bars.ToArray();
                var atrArray = _indicators.ATR(barsArray, 14);
                var closesArray = IndicatorService.ExtractCloses(barsArray);
                var sma200Array = _indicators.SMA(closesArray, 200);
                var cumulativeRsi2Array = _indicators.CumulativeRsi(
                    closesArray, _basePatternSettings.CumulativeRsi2.RsiPeriod,
                    _basePatternSettings.CumulativeRsi2.CumulativePeriod);
                var cumulativeRsi2TrendMaArray = _indicators.SMA(
                    closesArray, _basePatternSettings.CumulativeRsi2.LongTrendMaPeriod);

                var dateToIndex = new Dictionary<DateOnly, int>(barsArray.Length);
                for (int i = 0; i < barsArray.Length; i++)
                    dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                wfFullDataMap[symbol] = new SymbolPreparedData(
                    barsArray, atrArray, closesArray, sma200Array,
                    cumulativeRsi2Array, cumulativeRsi2TrendMaArray, dateToIndex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Walk-Forward 사전 로드 실패: {Symbol}", symbol);
            }
        }

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

    /// <summary>
    /// [E-2] 두 심볼의 최근 N봉 일간 수익률 Pearson 상관계수 계산.
    /// </summary>
    private static double ComputePearsonCorrelation(
        decimal[] closesA, Dictionary<DateOnly, int> idxA,
        decimal[] closesB, Dictionary<DateOnly, int> idxB,
        DateOnly refDate, int window)
    {
        // refDate 기준으로 최근 window+1개 공통 날짜 수집 → window개의 수익률
        var returnsA = new List<double>(window);
        var returnsB = new List<double>(window);

        var dates = idxA.Keys
            .Where(d => d <= refDate && idxB.ContainsKey(d))
            .OrderByDescending(d => d)
            .Take(window + 1)
            .OrderBy(d => d)
            .ToList();

        for (int i = 1; i < dates.Count; i++)
        {
            var d = dates[i];
            var prev = dates[i - 1];
            if (!idxA.TryGetValue(d, out var ia) || !idxA.TryGetValue(prev, out var ia0)) continue;
            if (!idxB.TryGetValue(d, out var ib) || !idxB.TryGetValue(prev, out var ib0)) continue;
            if (closesA[ia0] <= 0 || closesB[ib0] <= 0) continue;

            returnsA.Add((double)((closesA[ia] - closesA[ia0]) / closesA[ia0]));
            returnsB.Add((double)((closesB[ib] - closesB[ib0]) / closesB[ib0]));
        }

        int n = Math.Min(returnsA.Count, returnsB.Count);
        if (n < 10) return 0; // 데이터 부족 시 상관없음으로 처리

        double meanA = 0, meanB = 0;
        for (int i = 0; i < n; i++) { meanA += returnsA[i]; meanB += returnsB[i]; }
        meanA /= n; meanB /= n;

        double cov = 0, stdA = 0, stdB = 0;
        for (int i = 0; i < n; i++)
        {
            var da = returnsA[i] - meanA;
            var db = returnsB[i] - meanB;
            cov  += da * db;
            stdA += da * da;
            stdB += db * db;
        }

        var denom = Math.Sqrt(stdA * stdB);
        return denom > 0 ? cov / denom : 0;
    }

    /// <summary>백테스트 실행 시 사용할 리스크 파라미터 묶음</summary>
    internal sealed record RiskParams(
        decimal RiskPerTradePercent,
        decimal DailyLossLimitPercent,
        int MaxTotalPositions,
        int MaxPositionsPerSector
    );

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
    public async Task<Api.OptimizeResponse> RunOptimizationAsync(
        Api.OptimizeRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── 파라미터 조합 생성 (2단계 전략) ──
        var allCombinations = GenerateOptimizeCombinations(request.OptimizeParams);
        var totalCombinations = allCombinations.Count;

        // 2단계 전략: Coarse(60%) → Fine(40%)
        // 전체 조합이 maxCombinations 이하면 전부 실행 (1단계만)
        List<Api.OptimizeParamSnapshot> combinations;
        int stage2Budget = 0;
        if (allCombinations.Count <= request.MaxCombinations)
        {
            combinations = allCombinations;
        }
        else
        {
            // Stage 1: 60% 예산으로 랜덤 샘플링
            var stage1Budget = (int)(request.MaxCombinations * 0.6);
            stage2Budget = request.MaxCombinations - stage1Budget;
            combinations = allCombinations
                .OrderBy(_ => Random.Shared.Next())
                .Take(stage1Budget)
                .ToList();
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
            return new Api.OptimizeResponse
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

        var dataByTimeFrame = new Dictionary<Models.Enums.TimeFrame, Dictionary<string, SymbolPreparedData>>();

        foreach (var tf in timeFramesToLoad)
        {
            var warmupDays = tf switch
            {
                Models.Enums.TimeFrame.OneMinute     => 2,
                Models.Enums.TimeFrame.FiveMinute    => 10,
                Models.Enums.TimeFrame.FifteenMinute => 15,
                _                                    => 400
            };
            var tfDataMap = new Dictionary<string, SymbolPreparedData>();
            foreach (var symbol in request.Symbols)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fetchFrom = request.From.AddDays(-warmupDays);
                    var bars = await dataFeed.GetHistoricalBarsAsync(
                        symbol, tf, fetchFrom, request.To, ct);
                    if (bars.Count < TradeSimulator.MinWarmupBars) continue;

                    var barsArray   = bars.ToArray();
                    var atrArray    = _indicators.ATR(barsArray, 14);
                    var closesArray = IndicatorService.ExtractCloses(barsArray);
                    var sma200Array = _indicators.SMA(closesArray, 200);
                    var cumulativeRsi2Array = _indicators.CumulativeRsi(
                        closesArray, _basePatternSettings.CumulativeRsi2.RsiPeriod,
                        _basePatternSettings.CumulativeRsi2.CumulativePeriod);
                    var cumulativeRsi2TrendMaArray = _indicators.SMA(
                        closesArray, _basePatternSettings.CumulativeRsi2.LongTrendMaPeriod);

                    var dateToIndex = new Dictionary<DateOnly, int>(barsArray.Length);
                    for (int i = 0; i < barsArray.Length; i++)
                        dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                    tfDataMap[symbol] = new SymbolPreparedData(
                        barsArray, atrArray, closesArray, sma200Array,
                        cumulativeRsi2Array, cumulativeRsi2TrendMaArray, dateToIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "최적화 데이터 로드 실패: {Symbol}/{TF}", symbol, tf);
                }
            }
            if (tfDataMap.Count > 0)
                dataByTimeFrame[tf] = tfDataMap;
        }

        if (dataByTimeFrame.Count == 0)
        {
            _logger.LogWarning("최적화: 유효한 심볼 데이터 없음");
            return new Api.OptimizeResponse
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

        var riskParams = new RiskParams(
            RiskPerTradePercent: _tradingSettings.RiskPerTradePercent,
            DailyLossLimitPercent: _tradingSettings.DailyLossLimitPercent,
            MaxTotalPositions: _tradingSettings.MaxTotalPositions,
            MaxPositionsPerSector: _tradingSettings.MaxPositionsPerSector
        );

        // ── 조합별 백테스트 순차 실행 ──
        var resultItems = new List<Api.OptimizeResultItem>(combinations.Count);

        foreach (var combo in combinations)
        {
            ct.ThrowIfCancellationRequested();

            // 패턴 복사 + 파라미터 오버라이드 적용
            var patternCopy = ClonePatternDefinition(request.BasePattern);
            ApplyOptimizeOverrides(patternCopy, combo);

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

                resultItems.Add(new Api.OptimizeResultItem
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
            var stage1Top = RankOptimizeResults(resultItems, request.RankBy, 5);
            var neighbors = GenerateNeighborCombinations(stage1Top.Select(r => r.Params).ToList(),
                request.OptimizeParams, stage2Budget, allCombinations);

            _logger.LogInformation("Stage 2 정밀 탐색: {Count}개 이웃 조합 테스트", neighbors.Count);

            foreach (var combo in neighbors)
            {
                ct.ThrowIfCancellationRequested();
                var patternCopy = ClonePatternDefinition(request.BasePattern);
                ApplyOptimizeOverrides(patternCopy, combo);
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
                    resultItems.Add(new Api.OptimizeResultItem
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
        var ranked = RankOptimizeResults(resultItems, request.RankBy, request.MaxResults);

        // ── OOS 검증: 상위 N개에 대해 OOS 기간 재백테스트 ──
        if (hasOos)
        {
            foreach (var item in ranked)
            {
                ct.ThrowIfCancellationRequested();
                var patternCopy = ClonePatternDefinition(request.BasePattern);
                ApplyOptimizeOverrides(patternCopy, item.Params);
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

        return new Api.OptimizeResponse
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

    /// <summary>
    /// OptimizeParams로부터 모든 파라미터 조합(카르테시안 곱)을 생성합니다.
    /// 각 축을 Action&lt;OptimizeParamSnapshot&gt; 목록으로 동적 구성하여
    /// 파라미터 수가 늘어도 중첩 foreach 없이 확장 가능한 구조입니다.
    /// </summary>
    internal static List<Api.OptimizeParamSnapshot> GenerateOptimizeCombinations(Api.OptimizeParams p)
    {
        // 각 축: 가능한 setter 액션의 목록
        // 축이 설정되지 않으면 [null setter] 1개 = 해당 파라미터 오버라이드 없음
        var axes = new List<List<Action<Api.OptimizeParamSnapshot>>>();

        // ── 숫자형 단순 축 헬퍼 ──
        void AddNumericAxis(Api.ParamRange? range, Action<Api.OptimizeParamSnapshot, decimal?> setter)
        {
            var vals = range?.Enumerate().ToList();
            if (vals is { Count: > 0 })
                axes.Add(vals.Select(v => (Action<Api.OptimizeParamSnapshot>)(s => setter(s, v))).ToList());
            else
                axes.Add(new List<Action<Api.OptimizeParamSnapshot>> { _ => { } });
        }

        // ── 기존 5개 숫자형 축 ──
        AddNumericAxis(p.AtrStopMultiplier,   (s, v) => s.AtrStopMultiplier   = v);
        AddNumericAxis(p.AtrTargetMultiplier, (s, v) => s.AtrTargetMultiplier = v);
        AddNumericAxis(p.MaxHoldingBars,      (s, v) => s.MaxHoldingBars      = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.TrailingAtr,         (s, v) => s.TrailingAtr         = v);
        AddNumericAxis(p.PartialProfitR,      (s, v) => s.PartialProfitR      = v);

        // ── 추가 숫자형 축 ──
        AddNumericAxis(p.DefaultAllocationPercent,          (s, v) => s.DefaultAllocationPercent          = v);
        AddNumericAxis(p.CircuitBreakerConsecutiveLossLimit,(s, v) => s.CircuitBreakerConsecutiveLossLimit = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.CircuitBreakerCooldownBars,        (s, v) => s.CircuitBreakerCooldownBars        = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.CircuitBreakerMaxDrawdownPercent,  (s, v) => s.CircuitBreakerMaxDrawdownPercent  = v);
        AddNumericAxis(p.ReentryCooldownAfterLoss,          (s, v) => s.ReentryCooldownAfterLoss          = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.ReentryCooldownAfterWin,           (s, v) => s.ReentryCooldownAfterWin           = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.PortfolioMaxPositions,             (s, v) => s.PortfolioMaxPositions             = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.PortfolioMaxSinglePercent,         (s, v) => s.PortfolioMaxSinglePercent         = v);
        AddNumericAxis(p.PortfolioMaxEntriesPerDay,         (s, v) => s.PortfolioMaxEntriesPerDay         = v.HasValue ? (int)v.Value : (int?)null);

        // ── 카테고리형 축 ──
        void AddCategoryAxis<T>(List<T>? options, Action<Api.OptimizeParamSnapshot, T> setter)
        {
            if (options is { Count: > 0 })
                axes.Add(options.Select(v => (Action<Api.OptimizeParamSnapshot>)(s => setter(s, v))).ToList());
            else
                axes.Add(new List<Action<Api.OptimizeParamSnapshot>> { _ => { } });
        }

        AddCategoryAxis(p.EntryLogicOptions,        (s, v) => s.EntryLogic         = v);
        AddCategoryAxis(p.RequireBullRegimeOptions, (s, v) => s.RequireBullRegime  = v);
        AddCategoryAxis(p.EntryModeOptions,         (s, v) => s.EntryMode          = v);
        AddCategoryAxis(p.SizingModeOptions,        (s, v) => s.SizingMode         = v);
        AddCategoryAxis(p.ExitLogicOptions,         (s, v) => s.ExitLogic          = v);
        AddCategoryAxis(p.TimeFrameOptions,         (s, v) => s.TimeFrame          = v);

        // ── 룰 파라미터 오버라이드 축 (RuleParamOverrides) ──
        // 각 RuleParamRange를 독립 축으로 처리
        foreach (var dim in p.RuleParamOverrides ?? new List<Api.RuleParamRange>())
        {
            if (dim.Values.Count == 0) continue;
            var dimCopy = dim;
            axes.Add(dimCopy.Values.Select(val => (Action<Api.OptimizeParamSnapshot>)(s =>
            {
                s.RuleOverrides.Add(new Api.RuleOverrideEntry
                {
                    Scope     = dimCopy.Scope,
                    RuleIndex = dimCopy.RuleIndex,
                    ParamKey  = dimCopy.ParamKey,
                    Value     = val
                });
            })).ToList());
        }

        // ── 룰 필드 오버라이드 축 (RuleFieldOverrides) ──
        foreach (var dim in p.RuleFieldOverrides ?? new List<Api.RuleFieldRange>())
        {
            var dimCopy = dim;
            var setters = new List<Action<Api.OptimizeParamSnapshot>>();
            if (dimCopy.NumericValues is { Count: > 0 })
            {
                foreach (var val in dimCopy.NumericValues)
                {
                    var v = val;
                    setters.Add(s =>
                    {
                        s.RuleFieldOverrides ??= new List<Api.RuleFieldOverrideEntry>();
                        s.RuleFieldOverrides.Add(new Api.RuleFieldOverrideEntry
                        {
                            Scope        = dimCopy.Scope,
                            RuleIndex    = dimCopy.RuleIndex,
                            FieldName    = dimCopy.FieldName,
                            NumericValue = v
                        });
                    });
                }
            }
            if (dimCopy.StringValues is { Count: > 0 })
            {
                foreach (var val in dimCopy.StringValues)
                {
                    var v = val;
                    setters.Add(s =>
                    {
                        s.RuleFieldOverrides ??= new List<Api.RuleFieldOverrideEntry>();
                        s.RuleFieldOverrides.Add(new Api.RuleFieldOverrideEntry
                        {
                            Scope       = dimCopy.Scope,
                            RuleIndex   = dimCopy.RuleIndex,
                            FieldName   = dimCopy.FieldName,
                            StringValue = v
                        });
                    });
                }
            }
            if (setters.Count > 0)
                axes.Add(setters);
        }

        // ── 총 조합 수 계산 (오버플로우 방지) ──
        long totalCount = 1;
        foreach (var axis in axes)
        {
            totalCount *= axis.Count;
            if (totalCount > 1_000_000) // 100만 초과 시 조기 중단
            {
                totalCount = long.MaxValue;
                break;
            }
        }

        // ── 조합 수가 적으면 전체 카르테시안 곱 생성, 많으면 랜덤 샘플링 ──
        const int MaxFullGeneration = 50_000;

        if (totalCount <= MaxFullGeneration)
        {
            // 전체 생성 (기존 방식)
            var result = new List<Api.OptimizeParamSnapshot> { new() };
            foreach (var axis in axes)
            {
                var expanded = new List<Api.OptimizeParamSnapshot>(result.Count * axis.Count);
                foreach (var existing in result)
                {
                    foreach (var setter in axis)
                    {
                        var copy = CloneParamSnapshot(existing);
                        setter(copy);
                        expanded.Add(copy);
                    }
                }
                result = expanded;
            }
            return result;
        }
        else
        {
            // 랜덤 인덱스 샘플링: 메모리에 전체 조합을 올리지 않고 인덱스로 접근
            var axisSizes = axes.Select(a => a.Count).ToArray();
            var sampleCount = Math.Min(MaxFullGeneration, (int)Math.Min(totalCount, int.MaxValue));
            var sampled = new HashSet<string>();
            var result = new List<Api.OptimizeParamSnapshot>();

            // 실제 총 조합 수 (BigInteger 대신 double 근사)
            double realTotal = 1;
            foreach (var sz in axisSizes) realTotal *= sz;

            while (result.Count < sampleCount && sampled.Count < sampleCount * 3) // 무한루프 방지
            {
                // 랜덤 다차원 인덱스 생성
                var indices = new int[axisSizes.Length];
                for (int i = 0; i < axisSizes.Length; i++)
                    indices[i] = Random.Shared.Next(axisSizes[i]);

                var key = string.Join(",", indices);
                if (!sampled.Add(key)) continue; // 중복 스킵

                // 스냅샷 생성
                var snap = new Api.OptimizeParamSnapshot();
                for (int i = 0; i < axes.Count; i++)
                    axes[i][indices[i]](snap);

                result.Add(snap);
            }
            return result;
        }
    }

    /// <summary>
    /// OptimizeParamSnapshot을 깊은 복사합니다 (카르테시안 곱 생성 시 사용).
    /// </summary>
    private static Api.OptimizeParamSnapshot CloneParamSnapshot(Api.OptimizeParamSnapshot src)
    {
        return new Api.OptimizeParamSnapshot
        {
            AtrStopMultiplier                  = src.AtrStopMultiplier,
            AtrTargetMultiplier                = src.AtrTargetMultiplier,
            MaxHoldingBars                     = src.MaxHoldingBars,
            TrailingAtr                        = src.TrailingAtr,
            PartialProfitR                     = src.PartialProfitR,
            RuleOverrides                      = new List<Api.RuleOverrideEntry>(src.RuleOverrides),
            EntryLogic                         = src.EntryLogic,
            RequireBullRegime                  = src.RequireBullRegime,
            EntryMode                          = src.EntryMode,
            SizingMode                         = src.SizingMode,
            ExitLogic                          = src.ExitLogic,
            DefaultAllocationPercent           = src.DefaultAllocationPercent,
            CircuitBreakerConsecutiveLossLimit = src.CircuitBreakerConsecutiveLossLimit,
            CircuitBreakerCooldownBars         = src.CircuitBreakerCooldownBars,
            CircuitBreakerMaxDrawdownPercent   = src.CircuitBreakerMaxDrawdownPercent,
            ReentryCooldownAfterLoss           = src.ReentryCooldownAfterLoss,
            ReentryCooldownAfterWin            = src.ReentryCooldownAfterWin,
            PortfolioMaxPositions              = src.PortfolioMaxPositions,
            PortfolioMaxSinglePercent          = src.PortfolioMaxSinglePercent,
            PortfolioMaxEntriesPerDay          = src.PortfolioMaxEntriesPerDay,
            RuleFieldOverrides                 = src.RuleFieldOverrides != null
                ? new List<Api.RuleFieldOverrideEntry>(src.RuleFieldOverrides)
                : null,
        };
    }

    /// <summary>
    /// Stage 2: 상위 결과 주변에서 이웃 조합을 생성합니다.
    /// 각 숫자형 파라미터를 ±step 만큼 변형하여 정밀 탐색합니다.
    /// </summary>
    internal static List<Api.OptimizeParamSnapshot> GenerateNeighborCombinations(
        List<Api.OptimizeParamSnapshot> topSnapshots,
        Api.OptimizeParams paramDef,
        int budget,
        List<Api.OptimizeParamSnapshot> alreadyTested)
    {
        // 이미 테스트된 조합의 해시 (중복 방지)
        var testedKeys = new HashSet<string>(alreadyTested.Select(SnapshotKey));

        var neighbors = new List<Api.OptimizeParamSnapshot>();

        // 각 숫자형 파라미터의 step 값 수집
        var perturbations = new List<(Action<Api.OptimizeParamSnapshot, decimal> apply, Func<Api.OptimizeParamSnapshot, decimal?> get, decimal step)>();

        void AddPerturbation(Api.ParamRange? range, Func<Api.OptimizeParamSnapshot, decimal?> getter, Action<Api.OptimizeParamSnapshot, decimal> setter)
        {
            if (range == null) return;
            var step = range.Step ?? 1m;
            if (step <= 0) step = 1m;
            perturbations.Add((setter, getter, step));
        }

        AddPerturbation(paramDef.AtrStopMultiplier,   s => s.AtrStopMultiplier,   (s, v) => s.AtrStopMultiplier = v);
        AddPerturbation(paramDef.AtrTargetMultiplier,  s => s.AtrTargetMultiplier,  (s, v) => s.AtrTargetMultiplier = v);
        AddPerturbation(paramDef.MaxHoldingBars,       s => s.MaxHoldingBars,       (s, v) => s.MaxHoldingBars = (int)v);
        AddPerturbation(paramDef.TrailingAtr,          s => s.TrailingAtr,          (s, v) => s.TrailingAtr = v);
        AddPerturbation(paramDef.PartialProfitR,       s => s.PartialProfitR,       (s, v) => s.PartialProfitR = v);

        foreach (var snap in topSnapshots)
        {
            // 각 파라미터를 ±step 변형
            foreach (var (apply, get, step) in perturbations)
            {
                var currentVal = get(snap);
                if (currentVal == null) continue;

                foreach (var delta in new[] { -step, step, -step * 0.5m, step * 0.5m })
                {
                    var newVal = currentVal.Value + delta;
                    if (newVal < 0) continue;

                    var neighbor = CloneParamSnapshot(snap);
                    apply(neighbor, newVal);

                    var key = SnapshotKey(neighbor);
                    if (testedKeys.Contains(key)) continue;
                    testedKeys.Add(key);
                    neighbors.Add(neighbor);
                }
            }
        }

        // 예산 초과 시 랜덤 샘플링
        if (neighbors.Count > budget)
            neighbors = neighbors.OrderBy(_ => Random.Shared.Next()).Take(budget).ToList();

        return neighbors;
    }

    /// <summary>스냅샷의 간단한 해시키 (중복 검출용)</summary>
    private static string SnapshotKey(Api.OptimizeParamSnapshot s) =>
        $"{s.AtrStopMultiplier}|{s.AtrTargetMultiplier}|{s.MaxHoldingBars}|{s.TrailingAtr}|{s.PartialProfitR}" +
        $"|{s.EntryLogic}|{s.RequireBullRegime}|{s.EntryMode}|{s.SizingMode}|{s.ExitLogic}|{s.TimeFrame}" +
        $"|{s.DefaultAllocationPercent}|{s.CircuitBreakerConsecutiveLossLimit}|{s.PortfolioMaxPositions}" +
        $"|{string.Join(';', s.RuleOverrides.Select(r => $"{r.Scope}:{r.RuleIndex}:{r.ParamKey}:{r.Value}"))}" +
        $"|{string.Join(';', (s.RuleFieldOverrides ?? new List<Api.RuleFieldOverrideEntry>()).Select(r => $"{r.Scope}:{r.RuleIndex}:{r.FieldName}:{r.NumericValue}:{r.StringValue}"))}";

    /// <summary>
    /// RuleParamRange 목록에서 카르테시안 곱을 생성합니다.
    /// 빈 목록이면 빈 오버라이드 세트 하나를 반환합니다.
    /// </summary>
    private static List<List<Api.RuleOverrideEntry>> BuildRuleCombinations(
        List<Api.RuleParamRange> dims)
    {
        var result = new List<List<Api.RuleOverrideEntry>> { new() };

        foreach (var dim in dims)
        {
            if (dim.Values.Count == 0) continue;
            var expanded = new List<List<Api.RuleOverrideEntry>>();
            foreach (var existing in result)
            foreach (var val in dim.Values)
            {
                var copy = new List<Api.RuleOverrideEntry>(existing)
                {
                    new() { Scope = dim.Scope, RuleIndex = dim.RuleIndex, ParamKey = dim.ParamKey, Value = val }
                };
                expanded.Add(copy);
            }
            result = expanded;
        }

        return result;
    }

    /// <summary>
    /// 패턴 정의를 얕은 복사(JSON 필드는 문자열 복사)합니다.
    /// 최적화 루프에서 basePattern을 오염시키지 않기 위해 사용합니다.
    /// </summary>
    internal static CustomPatternDefinition ClonePatternDefinition(CustomPatternDefinition src)
    {
        return new CustomPatternDefinition
        {
            Id                   = src.Id,
            Name                 = src.Name,
            Description          = src.Description,
            EntryRulesJson       = src.EntryRulesJson,
            EntryLogic           = src.EntryLogic,
            RequireBullRegime    = src.RequireBullRegime,
            AtrStopMultiplier    = src.AtrStopMultiplier,
            AtrTargetMultiplier  = src.AtrTargetMultiplier,
            MaxHoldingBars       = src.MaxHoldingBars,
            TrailingAtr          = src.TrailingAtr,
            PartialProfitR       = src.PartialProfitR,
            UseWeightTiers       = src.UseWeightTiers,
            WeightTiersJson      = src.WeightTiersJson,
            DefaultAllocationPercent = src.DefaultAllocationPercent,
            ExitRulesJson        = src.ExitRulesJson,
            ExitRulesLogic       = src.ExitRulesLogic,
            ScalingRulesJson     = src.ScalingRulesJson,
            TimeFilterJson       = src.TimeFilterJson,
            CircuitBreakerJson   = src.CircuitBreakerJson,
            ReentryJson          = src.ReentryJson,
            PortfolioRulesJson   = src.PortfolioRulesJson,
            EntryGroupsJson      = src.EntryGroupsJson,
            EntryGroupsLogic     = src.EntryGroupsLogic,
            DynamicExitJson      = src.DynamicExitJson,
            EntryMode            = src.EntryMode,
            SizingMode           = src.SizingMode,
            IsActive             = src.IsActive,
            CreatedAt            = src.CreatedAt,
            UpdatedAt            = src.UpdatedAt,
        };
    }

    /// <summary>
    /// 파라미터 스냅샷을 패턴 정의에 적용합니다.
    /// null인 필드는 기존 값을 유지합니다.
    /// JSON 필드(CircuitBreaker, Reentry, PortfolioRules)는 파싱 후 필드를 수정하여 재직렬화합니다.
    /// </summary>
    internal static void ApplyOptimizeOverrides(CustomPatternDefinition pattern, Api.OptimizeParamSnapshot snap)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        static string NormalizeRuleScope(string? scope) =>
            string.Equals(scope, "Exit", StringComparison.OrdinalIgnoreCase) ? "Exit" : "Entry";

        static List<EntryRule>? GetOverrideTargets(CustomPatternDefinition pattern, JsonSerializerOptions jsonOpts, out bool fromGroups)
        {
            try
            {
                var groups = JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.EntryGroupsJson, jsonOpts);
                if (groups is { Count: > 0 })
                {
                    fromGroups = true;
                    return groups.SelectMany(group => group.Rules).ToList();
                }
            }
            catch
            {
                // group parsing failed, fall through to flat rules
            }

            try
            {
                fromGroups = false;
                return JsonSerializer.Deserialize<List<EntryRule>>(pattern.EntryRulesJson, jsonOpts);
            }
            catch
            {
                fromGroups = false;
                return null;
            }
        }

        static void SaveOverrideTargets(CustomPatternDefinition pattern, JsonSerializerOptions jsonOpts, bool fromGroups, List<EntryRule> flattenedRules)
        {
            if (fromGroups)
            {
                try
                {
                    var groups = JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.EntryGroupsJson, jsonOpts);
                    if (groups is { Count: > 0 })
                    {
                        var index = 0;
                        foreach (var group in groups)
                        {
                            for (var i = 0; i < group.Rules.Count && index < flattenedRules.Count; i++, index++)
                                group.Rules[i] = flattenedRules[index];
                        }

                        pattern.EntryGroupsJson = JsonSerializer.Serialize(groups);
                    }
                }
                catch
                {
                    // keep original group JSON on failure
                }

                return;
            }

            pattern.EntryRulesJson = JsonSerializer.Serialize(flattenedRules);
        }

        static List<EntryRule>? GetExitOverrideTargets(CustomPatternDefinition pattern, JsonSerializerOptions jsonOpts)
        {
            try
            {
                return JsonSerializer.Deserialize<List<EntryRule>>(pattern.ExitRulesJson, jsonOpts);
            }
            catch
            {
                return null;
            }
        }

        static void SaveExitOverrideTargets(CustomPatternDefinition pattern, List<EntryRule> rules)
        {
            pattern.ExitRulesJson = JsonSerializer.Serialize(rules);
        }

        // ── 기존 숫자형 파라미터 ──
        if (snap.AtrStopMultiplier.HasValue)   pattern.AtrStopMultiplier   = snap.AtrStopMultiplier.Value;
        if (snap.AtrTargetMultiplier.HasValue) pattern.AtrTargetMultiplier = snap.AtrTargetMultiplier.Value;
        if (snap.MaxHoldingBars.HasValue)      pattern.MaxHoldingBars      = snap.MaxHoldingBars.Value;
        if (snap.TrailingAtr.HasValue)         pattern.TrailingAtr         = snap.TrailingAtr.Value;
        if (snap.PartialProfitR.HasValue)      pattern.PartialProfitR      = snap.PartialProfitR.Value;

        // ── 카테고리형 파라미터 ──
        if (snap.EntryLogic        != null) pattern.EntryLogic       = snap.EntryLogic;
        if (snap.RequireBullRegime.HasValue) pattern.RequireBullRegime = snap.RequireBullRegime.Value;
        if (snap.EntryMode         != null) pattern.EntryMode        = snap.EntryMode;
        if (snap.SizingMode        != null) pattern.SizingMode       = snap.SizingMode;
        if (snap.ExitLogic         != null) pattern.ExitRulesLogic   = snap.ExitLogic;

        // ── 기본 비중 ──
        if (snap.DefaultAllocationPercent.HasValue)
            pattern.DefaultAllocationPercent = snap.DefaultAllocationPercent.Value;

        // ── CircuitBreakerJson 파싱 → 수정 → 재직렬화 ──
        if (snap.CircuitBreakerConsecutiveLossLimit.HasValue
            || snap.CircuitBreakerCooldownBars.HasValue
            || snap.CircuitBreakerMaxDrawdownPercent.HasValue)
        {
            try
            {
                var cb = JsonSerializer.Deserialize<CircuitBreakerConfig>(pattern.CircuitBreakerJson, jsonOpts) ?? new();
                if (snap.CircuitBreakerConsecutiveLossLimit.HasValue) cb.ConsecutiveLossLimit = snap.CircuitBreakerConsecutiveLossLimit.Value;
                if (snap.CircuitBreakerCooldownBars.HasValue)         cb.CooldownBars         = snap.CircuitBreakerCooldownBars.Value;
                if (snap.CircuitBreakerMaxDrawdownPercent.HasValue)   cb.MaxDrawdownPercent   = snap.CircuitBreakerMaxDrawdownPercent.Value;
                pattern.CircuitBreakerJson = JsonSerializer.Serialize(cb);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── ReentryJson 파싱 → 수정 → 재직렬화 ──
        if (snap.ReentryCooldownAfterLoss.HasValue || snap.ReentryCooldownAfterWin.HasValue)
        {
            try
            {
                var rc = JsonSerializer.Deserialize<ReentryConfig>(pattern.ReentryJson, jsonOpts) ?? new();
                if (snap.ReentryCooldownAfterLoss.HasValue) rc.CooldownBarsAfterLoss = snap.ReentryCooldownAfterLoss.Value;
                if (snap.ReentryCooldownAfterWin.HasValue)  rc.CooldownBarsAfterWin  = snap.ReentryCooldownAfterWin.Value;
                pattern.ReentryJson = JsonSerializer.Serialize(rc);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── PortfolioRulesJson 파싱 → 수정 → 재직렬화 ──
        if (snap.PortfolioMaxPositions.HasValue
            || snap.PortfolioMaxSinglePercent.HasValue
            || snap.PortfolioMaxEntriesPerDay.HasValue)
        {
            try
            {
                var pr = JsonSerializer.Deserialize<PortfolioRulesConfig>(pattern.PortfolioRulesJson, jsonOpts) ?? new();
                if (snap.PortfolioMaxPositions.HasValue)    pr.MaxTotalPositions       = snap.PortfolioMaxPositions.Value;
                if (snap.PortfolioMaxSinglePercent.HasValue) pr.MaxSinglePositionPercent = snap.PortfolioMaxSinglePercent.Value;
                if (snap.PortfolioMaxEntriesPerDay.HasValue) pr.MaxEntriesPerDay         = snap.PortfolioMaxEntriesPerDay.Value;
                pattern.PortfolioRulesJson = JsonSerializer.Serialize(pr);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── RuleParamOverrides / RuleFieldOverrides: 활성 진입/청산 규칙 수정 → 재직렬화 ──
        if (snap.RuleOverrides.Count > 0)
        {
            foreach (var scopeGroup in snap.RuleOverrides.GroupBy(entry => NormalizeRuleScope(entry.Scope)))
            {
                try
                {
                    if (scopeGroup.Key == "Exit")
                    {
                        var rules = GetExitOverrideTargets(pattern, jsonOpts);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var paramKey = entry.ParamKey ?? string.Empty;
                            if (paramKey.StartsWith("compare.", StringComparison.OrdinalIgnoreCase))
                            {
                                var compareKey = paramKey["compare.".Length..];
                                rules[entry.RuleIndex].CompareParams[compareKey] = entry.Value;
                            }
                            else
                            {
                                rules[entry.RuleIndex].Params[paramKey] = entry.Value;
                            }
                        }
                        SaveExitOverrideTargets(pattern, rules);
                    }
                    else
                    {
                        var rules = GetOverrideTargets(pattern, jsonOpts, out var fromGroups);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var paramKey = entry.ParamKey ?? string.Empty;
                            if (paramKey.StartsWith("compare.", StringComparison.OrdinalIgnoreCase))
                            {
                                var compareKey = paramKey["compare.".Length..];
                                rules[entry.RuleIndex].CompareParams[compareKey] = entry.Value;
                            }
                            else
                            {
                                rules[entry.RuleIndex].Params[paramKey] = entry.Value;
                            }
                        }
                        SaveOverrideTargets(pattern, jsonOpts, fromGroups, rules);
                    }
                }
                catch { /* JSON 파싱 실패 시 룰 오버라이드 없이 진행 */ }
            }
        }

        if (snap.RuleFieldOverrides is { Count: > 0 })
        {
            foreach (var scopeGroup in snap.RuleFieldOverrides.GroupBy(entry => NormalizeRuleScope(entry.Scope)))
            {
                try
                {
                    if (scopeGroup.Key == "Exit")
                    {
                        var rules = GetExitOverrideTargets(pattern, jsonOpts);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var rule = rules[entry.RuleIndex];
                            switch (entry.FieldName.ToLowerInvariant())
                            {
                                case "value"           when entry.NumericValue.HasValue:
                                    rule.Value = entry.NumericValue.Value; break;
                                case "withinbars"      when entry.NumericValue.HasValue:
                                    rule.WithinBars = (int)entry.NumericValue.Value; break;
                                case "weight"          when entry.NumericValue.HasValue:
                                    rule.Weight = entry.NumericValue.Value; break;
                                case "consecutivebars" when entry.NumericValue.HasValue:
                                    rule.ConsecutiveBars = (int)entry.NumericValue.Value; break;
                                case "operator"        when entry.StringValue != null:
                                    rule.Operator = entry.StringValue; break;
                                case "compareindicator" when entry.StringValue != null:
                                    rule.CompareIndicator = entry.StringValue; break;
                            }
                        }
                        SaveExitOverrideTargets(pattern, rules);
                    }
                    else
                    {
                        var rules = GetOverrideTargets(pattern, jsonOpts, out var fromGroups);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var rule = rules[entry.RuleIndex];
                            switch (entry.FieldName.ToLowerInvariant())
                            {
                                case "value"           when entry.NumericValue.HasValue:
                                    rule.Value = entry.NumericValue.Value; break;
                                case "withinbars"      when entry.NumericValue.HasValue:
                                    rule.WithinBars = (int)entry.NumericValue.Value; break;
                                case "weight"          when entry.NumericValue.HasValue:
                                    rule.Weight = entry.NumericValue.Value; break;
                                case "consecutivebars" when entry.NumericValue.HasValue:
                                    rule.ConsecutiveBars = (int)entry.NumericValue.Value; break;
                                case "operator"        when entry.StringValue != null:
                                    rule.Operator = entry.StringValue; break;
                                case "compareindicator" when entry.StringValue != null:
                                    rule.CompareIndicator = entry.StringValue; break;
                            }
                        }
                        SaveOverrideTargets(pattern, jsonOpts, fromGroups, rules);
                    }
                }
                catch { /* JSON 파싱 실패 시 필드 오버라이드 없이 진행 */ }
            }
        }
    }

    /// <summary>
    /// 결과 목록을 rankBy 기준으로 정렬하고 상위 maxResults개에 순위를 매깁니다.
    /// </summary>
    internal static List<Api.OptimizeResultItem> RankOptimizeResults(
        List<Api.OptimizeResultItem> items, string rankBy, int maxResults)
    {
        IEnumerable<Api.OptimizeResultItem> sorted = rankBy.ToLowerInvariant() switch
        {
            "totalreturn"      => items.OrderByDescending(r => r.TotalReturn),
            "sharperation" or
            "sharperatio"      => items.OrderByDescending(r => r.SharpeRatio),
            "calmarratio"      => items.OrderByDescending(r => r.CalmarRatio),
            "profitfactor"     => items.OrderByDescending(r => r.ProfitFactor),
            "winrate"          => items.OrderByDescending(r => r.WinRate),
            "annualizedreturn" => items.OrderByDescending(r => r.AnnualizedReturn),
            _                  => items.OrderByDescending(r => r.SortinoRatio) // 기본: sortinoRatio
        };

        var ranked = sorted.Take(maxResults).ToList();
        for (int i = 0; i < ranked.Count; i++)
            ranked[i].Rank = i + 1;

        return ranked;
    }

    #endregion
}

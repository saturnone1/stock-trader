using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 준비된 시세 데이터를 날짜순으로 실행해 체결, 포트폴리오 상태와 성과 결과를 생성합니다.
/// 데이터 조회와 최적화 조정 책임은 포함하지 않습니다.
/// </summary>
public sealed class BacktestSimulationEngine
{
    private readonly ILogger<BacktestSimulationEngine> _logger;

    public BacktestSimulationEngine(ILogger<BacktestSimulationEngine> logger)
    {
        _logger = logger;
    }

    internal async Task<BacktestResult> RunAsync(
        List<string> symbols,
        IReadOnlyDictionary<string, PreparedSymbolData> symbolDataMap,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from, DateTime to,
        decimal initialCapital,
        decimal slippagePercent, decimal commissionPerTrade,
        TimeFrame timeFrame,
        BacktestRiskParameters riskParams,
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
        var allDates = BacktestTimeline.Build(symbolDataMap.Values, from);

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
        Dictionary<string, CustomStrategyRuntime> strategyRuntimes = null!;
        var executionCosts = new BacktestExecutionCostLedger(
            slippageModel, slippagePercent, commissionPerTrade);

        void ApplyNewTradeCosts(int startIndex)
        {
            executionCosts.ApplyNewTrades(trades, startIndex, trade =>
            {
                currentEquity += trade.PnL;

                if (!string.IsNullOrWhiteSpace(trade.CustomPatternName)
                    && strategyRuntimes != null
                    && strategyRuntimes.TryGetValue(trade.CustomPatternName, out var runtime))
                {
                    runtime.RealizedEquity += trade.PnL;
                    if (runtime.RealizedEquity > runtime.PeakEquity)
                        runtime.PeakEquity = runtime.RealizedEquity;
                    if (runtime.CircuitBreaker.MaxDrawdownPercent > 0 && runtime.PeakEquity > 0)
                    {
                        var drawdownPercent = (runtime.PeakEquity - runtime.RealizedEquity)
                            / runtime.PeakEquity * 100m;
                        if (drawdownPercent >= runtime.CircuitBreaker.MaxDrawdownPercent)
                            runtime.CircuitBreakerTripped = true;
                    }
                }
            });
        }
        // [A-1] NextOpen 진입 대기 시그널: (symbol → pending signal 정보)
        var pendingNextOpenSignals = new Dictionary<string, (decimal entryPrice, decimal stopLoss, decimal target, decimal stopDistance, decimal entryAtr, long entryVolume, decimal equityAtEntry, TradeSimulator.PatternExitProfile? customExit, decimal riskPerTradeSnap, decimal effectiveMaxPosSnap, string? customPatternName)>();
        var maxWindow = BacktestTimeFramePolicy.Get(timeFrame).SimulationWindowBars;

        // ── 커스텀 패턴 고급 기능: 상태 추적 ──
        // 서킷브레이커, 재진입 쿨다운, 스케일링 등에 사용
        var customDetectors = detectors.OfType<RuleBasedDetector>().ToList();
        var customDetectorsByName = customDetectors
            .GroupBy(detector => detector.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        strategyRuntimes = customDetectorsByName.ToDictionary(
            pair => pair.Key,
            pair => new CustomStrategyRuntime
            {
                Detector = pair.Value,
                CircuitBreaker = pair.Value.Strategy.CircuitBreaker,
                Reentry = pair.Value.Strategy.Reentry,
                Portfolio = pair.Value.Strategy.PortfolioRules,
                RealizedEquity = initialCapital,
                PeakEquity = initialCapital
            },
            StringComparer.OrdinalIgnoreCase);

        // 전략+종목별 재진입 쿨다운
        var reentryCooldowns = new Dictionary<string, int>();
        // 스케일링 횟수 추적: (symbol → rule index → count)
        var positionScaleCounts = new Dictionary<string, Dictionary<int, int>>();
        var latestPrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var equityCurve = new List<EquityPoint> { new(from, initialCapital) };
        var peakMarkedEquity = initialCapital;
        var maxDrawdown = 0m;

        void RecordMarkedEquity(DateTime timestamp)
        {
            var unrealizedPnl = openPositions.Sum(pair =>
                latestPrices.TryGetValue(pair.Key, out var price)
                    ? (price - pair.Value.EntryPrice)
                        * (pair.Value.CurrentQuantity > 0 ? pair.Value.CurrentQuantity : pair.Value.Quantity)
                    : 0m);
            var markedEquity = currentEquity + unrealizedPnl;
            if (markedEquity > peakMarkedEquity) peakMarkedEquity = markedEquity;
            var drawdown = peakMarkedEquity > 0
                ? (peakMarkedEquity - markedEquity) / peakMarkedEquity
                : 0m;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;

            if (equityCurve.Count > 0 && equityCurve[^1].Date == timestamp)
                equityCurve[^1] = new EquityPoint(timestamp, markedEquity);
            else
                equityCurve.Add(new EquityPoint(timestamp, markedEquity));
        }

        // ── 참조 종목 데이터 준비 (RefSymbol 지원) ──
        Dictionary<string, OhlcvBar[]>? referenceData = null;
        if (customDetectors.Count > 0)
        {
            referenceData = new Dictionary<string, OhlcvBar[]>();
            foreach (var (sym, sd) in symbolDataMap)
                referenceData[sym.ToUpperInvariant()] = sd.Bars;
        }

        for (var timelineIndex = 0; timelineIndex < allDates.Count; timelineIndex++)
        {
            var date = allDates[timelineIndex];
            var tradingDay = DateOnly.FromDateTime(date);
            ct.ThrowIfCancellationRequested();
            foreach (var (symbol, data) in symbolDataMap)
            {
                if (data.TimestampToIndex.TryGetValue(date, out var priceBarIndex))
                    latestPrices[symbol] = data.Bars[priceBarIndex].Close;
            }
            if (referenceData != null)
            {
                var referenceAsOf = date;
                foreach (var detector in customDetectors)
                    detector.SetReferenceData(referenceData, referenceAsOf);
            }
            var regime = TradeSimulator.GetRegimeForDate(tradingDay, regimeByDate);

            if (tradingDay != dailyLossDate)
            {
                dailyLossDate = tradingDay;
                dailyStartEquity = currentEquity;
            }

            foreach (var runtime in strategyRuntimes.Values)
            {
                if (runtime.LastEntryDate != tradingDay) runtime.DailyEntryCount = 0;
            }

            // ── 2a. 보유 중인 모든 포지션의 청산 로직 ──
            foreach (var symbol in openPositions.Keys.ToList())
            {
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.TimestampToIndex.TryGetValue(date, out var barIdx)) continue;

                var pos = openPositions[symbol];
                var tradesBefore = trades.Count;
                var positionDetector = pos.CustomPatternName != null
                    && customDetectorsByName.TryGetValue(pos.CustomPatternName, out var matchedDetector)
                        ? matchedDetector
                        : null;
                var positionRuntime = pos.CustomPatternName != null
                    && strategyRuntimes.TryGetValue(pos.CustomPatternName, out var matchedRuntime)
                        ? matchedRuntime
                        : null;

                // 장중 가격으로 체결되는 손절/목표가를 종가 규칙과 분할매매보다 먼저 평가한다.
                tradesBefore = trades.Count;
                var exitResult = simulator.ProcessExitLogic(
                    pos, sd.Bars[barIdx], barIdx,
                    sd.Atr[barIdx], sd.Sma200[barIdx],
                    sd.CumulativeRsi2[barIdx], sd.CumulativeRsi2TrendMa[barIdx], cumulativeRsi2Config,
                    pepCache, exitOverrides, symbol, trades);
                ApplyNewTradeCosts(tradesBefore);

                if (exitResult == null)
                {
                    openPositions.Remove(symbol);
                    positionScaleCounts.Remove(symbol);
                    // 재진입 쿨다운 등록
                    if (trades.Count > tradesBefore)
                    {
                        if (positionRuntime != null)
                        {
                            RegisterCooldown($"{pos.CustomPatternName}|{symbol}", barIdx, trades[^1], positionRuntime.Reentry, reentryCooldowns);
                            UpdateCircuitBreaker(trades[^1], ref positionRuntime.ConsecutiveLosses, ref positionRuntime.CircuitBreakerUntilStep,
                                timelineIndex, positionRuntime.CircuitBreaker);
                        }
                    }
                }
                else
                {
                    pos = exitResult;
                    openPositions[symbol] = pos;

                    var windowSize = Math.Min(barIdx + 1, maxWindow);
                    var windowStart = barIdx + 1 - windowSize;
                    var windowBars = sd.Bars[windowStart..(barIdx + 1)];

                    // 종가로 판단하는 사용자 청산 규칙은 장중 스탑/목표가를 통과한 뒤 적용한다.
                    if (positionDetector != null && positionDetector.HasExitRules
                        && positionDetector.ShouldExit(windowBars))
                    {
                        tradesBefore = trades.Count;
                        trades.Add(TradeSimulator.CreateTradeRecord(
                            symbol, pos, sd.Bars[barIdx].Close, sd.Bars[barIdx].Timestamp,
                            "규칙 청산", pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity));
                        ApplyNewTradeCosts(tradesBefore);
                        openPositions.Remove(symbol);
                        positionScaleCounts.Remove(symbol);
                        if (positionRuntime != null)
                        {
                            RegisterCooldown($"{pos.CustomPatternName}|{symbol}", barIdx, trades[^1], positionRuntime.Reentry, reentryCooldowns);
                            UpdateCircuitBreaker(trades[^1], ref positionRuntime.ConsecutiveLosses, ref positionRuntime.CircuitBreakerUntilStep,
                                timelineIndex, positionRuntime.CircuitBreaker);
                        }
                        continue;
                    }

                    // 추가 매수/분할 매도는 종가에서만 실행하며, 같은 봉의 이전 저가/고가에 소급 적용하지 않는다.
                    if (positionDetector != null && positionDetector.HasScalingRules)
                    {
                        var currentProfitPct = pos.EntryPrice > 0
                            ? (sd.Bars[barIdx].Close - pos.EntryPrice) / pos.EntryPrice * 100
                            : 0;
                        if (!positionScaleCounts.TryGetValue(symbol, out var scaleCounts))
                        {
                            scaleCounts = new Dictionary<int, int>();
                            positionScaleCounts[symbol] = scaleCounts;
                        }
                        var matchedScale = positionDetector.CheckScaling(windowBars, currentProfitPct, scaleCounts);
                        if (matchedScale != null)
                        {
                            var scaleQty = Math.Max(1, (int)(pos.Quantity * matchedScale.Percent / 100m));
                            if (matchedScale.Direction == "SCALE_IN")
                            {
                                var scaleCapRatio = maxTotalPositions > 0 ? 1m / maxTotalPositions : 0.10m;
                                if (positionRuntime?.Portfolio.MaxSinglePositionPercent > 0)
                                    scaleCapRatio = Math.Min(scaleCapRatio,
                                        positionRuntime.Portfolio.MaxSinglePositionPercent / 100m);
                                var remainingCapital = Math.Max(0m,
                                    currentEquity * scaleCapRatio - pos.TotalCost);
                                var affordableScaleQty = sd.Bars[barIdx].Close > 0
                                    ? (int)(remainingCapital / sd.Bars[barIdx].Close)
                                    : 0;
                                scaleQty = Math.Min(scaleQty, affordableScaleQty);
                                if (scaleQty <= 0) continue;
                                var currentQty = pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity;
                                var newQty = currentQty + scaleQty;
                                var newTotalCost = pos.TotalCost + sd.Bars[barIdx].Close * scaleQty;
                                pos.CurrentQuantity = newQty;
                                pos.TotalCost = newTotalCost;
                                pos.EntryPrice = newTotalCost / newQty;
                            }
                            else
                            {
                                var currentQty = pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity;
                                var sellQty = Math.Min(scaleQty, currentQty - 1);
                                if (sellQty > 0)
                                {
                                    tradesBefore = trades.Count;
                                    trades.Add(TradeSimulator.CreateTradeRecord(
                                        symbol, pos, sd.Bars[barIdx].Close,
                                        sd.Bars[barIdx].Timestamp, $"분할 매도({matchedScale.Percent}%)", sellQty));
                                    pos.CurrentQuantity = currentQty - sellQty;
                                    pos.TotalCost = pos.EntryPrice * pos.CurrentQuantity;
                                    ApplyNewTradeCosts(tradesBefore);
                                }
                            }
                        }
                    }
                }
            }

            // ── 전략별 피크 에퀴티 + 최대낙폭 거래 중단 체크 ──
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
                    if (!pendingSd.TimestampToIndex.TryGetValue(date, out var pendingBarIdx)) continue;

                    var pending = pendingNextOpenSignals[pendingSymbol];
                    var pendingRuntime = pending.customPatternName != null
                        && strategyRuntimes.TryGetValue(pending.customPatternName, out var resolvedPendingRuntime)
                            ? resolvedPendingRuntime
                            : null;
                    var pendingPositionLimit = pendingRuntime?.Portfolio.MaxTotalPositions > 0
                        ? Math.Min(maxTotalPositions, pendingRuntime.Portfolio.MaxTotalPositions)
                        : maxTotalPositions;
                    if (openPositions.Count >= pendingPositionLimit
                        || pendingRuntime?.CircuitBreakerTripped == true
                        || (pendingRuntime != null && pendingRuntime.CircuitBreaker.ConsecutiveLossLimit > 0 && timelineIndex < pendingRuntime.CircuitBreakerUntilStep)
                        || (pendingRuntime != null && pendingRuntime.Portfolio.MaxEntriesPerDay > 0 && pendingRuntime.DailyEntryCount >= pendingRuntime.Portfolio.MaxEntriesPerDay)
                        || (pending.customPatternName != null && reentryCooldowns.TryGetValue($"{pending.customPatternName}|{pendingSymbol}", out var pendingCooldown) && pendingBarIdx < pendingCooldown))
                    {
                        pendingNextOpenSignals.Remove(pendingSymbol);
                        continue;
                    }
                    var nextOpen = pendingSd.Bars[pendingBarIdx].Open;
                    if (nextOpen <= 0) { pendingNextOpenSignals.Remove(pendingSymbol); continue; }

                    // NextOpen에서도 미리보기와 동일하게 원 신호의 위험 거리/R 배수를 보존한다.
                    var fill = LongEntryFillPolicy.Reprice(
                        pending.entryPrice,
                        pending.stopLoss,
                        pending.target,
                        nextOpen,
                        fallbackTargetMultiple: 2m);
                    if (fill is null)
                    {
                        pendingNextOpenSignals.Remove(pendingSymbol);
                        continue;
                    }

                    var pendingRiskAmount = pending.equityAtEntry * pending.riskPerTradeSnap;
                    var pendingQty = (int)(pendingRiskAmount / fill.RiskDistance);
                    if (pendingQty <= 0) pendingQty = 1;
                    var pendingMaxQty = pending.effectiveMaxPosSnap > 0
                        ? (int)(pending.equityAtEntry * pending.effectiveMaxPosSnap / nextOpen)
                        : 0;
                    if (pendingMaxQty > 0) pendingQty = Math.Min(pendingQty, pendingMaxQty);

                    var pendingPosition = new TradeSimulator.OpenPosition
                    {
                        PatternType           = PatternType.Custom,
                        CustomPatternName     = pending.customPatternName,
                        EntryPrice            = nextOpen,
                        OriginalStop          = fill.StopPrice,
                        StopLoss              = fill.StopPrice,
                        Target                = fill.TargetPrice,
                        Quantity              = pendingQty,
                        CurrentQuantity       = pendingQty,
                        TotalCost             = nextOpen * pendingQty,
                        EntryTime             = pendingSd.Bars[pendingBarIdx].Timestamp,
                        EntryBarIndex         = pendingBarIdx,
                        EntryAtr              = pending.entryAtr,
                        EntryVolume           = pending.entryVolume,
                        HighestHighSinceEntry = nextOpen,
                        LowestLowSinceEntry   = nextOpen,
                        RiskDistance          = fill.RiskDistance,
                        EquityAtEntry         = pending.equityAtEntry,
                        CustomExitProfile     = pending.customExit
                    };

                    // 다음 봉 시가에 진입했으므로 해당 진입 봉의 저가/고가도 실제 보유 구간이다.
                    // 진입 봉을 건너뛰어 손절을 피하는 낙관적 편향을 방지한다.
                    var pendingTradesBefore = trades.Count;
                    var pendingExitResult = simulator.ProcessExitLogic(
                        pendingPosition, pendingSd.Bars[pendingBarIdx], pendingBarIdx,
                        pendingSd.Atr[pendingBarIdx], pendingSd.Sma200[pendingBarIdx],
                        pendingSd.CumulativeRsi2[pendingBarIdx], pendingSd.CumulativeRsi2TrendMa[pendingBarIdx],
                        cumulativeRsi2Config, pepCache, exitOverrides, pendingSymbol, trades);
                    ApplyNewTradeCosts(pendingTradesBefore);
                    if (pendingExitResult != null)
                    {
                        openPositions[pendingSymbol] = pendingExitResult;
                    }
                    else if (pendingRuntime != null && trades.Count > pendingTradesBefore)
                    {
                        RegisterCooldown($"{pending.customPatternName}|{pendingSymbol}", pendingBarIdx,
                            trades[^1], pendingRuntime.Reentry, reentryCooldowns);
                        UpdateCircuitBreaker(trades[^1], ref pendingRuntime.ConsecutiveLosses,
                            ref pendingRuntime.CircuitBreakerUntilStep, timelineIndex, pendingRuntime.CircuitBreaker);
                    }

                    pendingNextOpenSignals.Remove(pendingSymbol);
                    if (pendingRuntime != null)
                    {
                        pendingRuntime.DailyEntryCount++;
                        pendingRuntime.LastEntryDate = tradingDay;
                    }
                }
            }

            // ── 2b. 새 진입 ──
            if (dailyLossLimitReached)
            {
                RecordMarkedEquity(date);
                continue;
            }

            if (openPositions.Count >= maxTotalPositions)
            {
                RecordMarkedEquity(date);
                continue;
            }

            foreach (var symbol in symbols)
            {
                if (openPositions.ContainsKey(symbol)) continue;
                if (openPositions.Count >= maxTotalPositions) break;
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.TimestampToIndex.TryGetValue(date, out var barIdx)) continue;
                if (barIdx < BacktestDataPolicy.MinimumWarmupBars) continue;

                var windowSize = Math.Min(barIdx + 1, maxWindow);
                var windowStart = barIdx + 1 - windowSize;
                var windowBars = sd.Bars[windowStart..(barIdx + 1)];

                foreach (var detector in detectors)
                {
                    try
                    {
                        var ruleDetector = detector as RuleBasedDetector;
                        var strategyRuntime = ruleDetector != null
                            && strategyRuntimes.TryGetValue(ruleDetector.Definition.Name, out var configuredRuntime)
                                ? configuredRuntime
                                : null;
                        var portfolioRules = strategyRuntime?.Portfolio;
                        var effectiveMaxPos = maxTotalPositions;
                        if (portfolioRules?.MaxTotalPositions > 0)
                            effectiveMaxPos = Math.Min(effectiveMaxPos, portfolioRules.MaxTotalPositions);
                        if (openPositions.Count >= effectiveMaxPos) continue;
                        if (strategyRuntime?.CircuitBreakerTripped == true) continue;
                        if (strategyRuntime != null
                            && strategyRuntime.CircuitBreaker.ConsecutiveLossLimit > 0
                            && timelineIndex < strategyRuntime.CircuitBreakerUntilStep) continue;
                        if (strategyRuntime != null
                            && strategyRuntime.Portfolio.MaxEntriesPerDay > 0
                            && strategyRuntime.DailyEntryCount >= strategyRuntime.Portfolio.MaxEntriesPerDay) continue;
                        if (ruleDetector != null
                            && reentryCooldowns.TryGetValue($"{ruleDetector.Definition.Name}|{symbol}", out var cooldownUntil)
                            && barIdx < cooldownUntil) continue;

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
                                    existingSd.Closes, existingSd.TimestampToIndex,
                                    newSd.Closes, newSd.TimestampToIndex,
                                    date, corrWindow);

                                if (corrCorr > (double)portfolioRules.MaxCorrelation)
                                {
                                    correlationBlocked = true;
                                    break;
                                }
                            }
                            if (correlationBlocked) continue;
                        }

                        // 완료된 과거 거래만 사용해 켈리 비율 계산 (미래 거래 누출 방지)
                        var effectiveRisk = riskPerTrade;
                        if (detector is RuleBasedDetector rbdSizing)
                        {
                            var sizingTrades = trades
                                .Where(trade => string.Equals(
                                    trade.CustomPatternName,
                                    rbdSizing.Definition.Name,
                                    StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            decimal rollingKelly = 0;
                            if (sizingTrades.Count >= 10)
                            {
                                var completedWins = sizingTrades.Where(trade => trade.PnL > 0).ToList();
                                var completedLosses = sizingTrades.Where(trade => trade.PnL < 0).ToList();
                                var rollingWinRate = (decimal)completedWins.Count / sizingTrades.Count;
                                var rollingAvgWin = completedWins.Count > 0 ? completedWins.Average(trade => trade.PnLPercent * 100) : 0;
                                var rollingAvgLoss = completedLosses.Count > 0 ? Math.Abs(completedLosses.Average(trade => trade.PnLPercent * 100)) : 0;
                                rollingKelly = PerformanceCalculator.ComputeKellyFraction(rollingWinRate, rollingAvgWin, rollingAvgLoss);
                            }
                            var sizingMode = rbdSizing.Definition.SizingMode;
                            if (sizingMode == "Kelly" && rollingKelly > 0)
                                effectiveRisk = rollingKelly;
                            else if (sizingMode == "HalfKelly" && rollingKelly > 0)
                                effectiveRisk = rollingKelly / 2;
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
                        if (ruleDetector != null)
                        {
                            var def = ruleDetector.Definition;
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
                        var entryDefinition = ruleDetector?.Definition;
                        bool isNextOpen = entryDefinition?.EntryMode == "NextOpen";

                        if (isNextOpen && !pendingNextOpenSignals.ContainsKey(symbol))
                        {
                            // 이번 봉에서 시그널만 저장하고, 다음 봉 Open에서 진입
                            signal.PendingEntry = true;
                            pendingNextOpenSignals[symbol] = (
                                signal.EntryPrice,
                                signal.StopLossPrice,
                                signal.TargetPrice,
                                stopDistance,
                                entryAtr,
                                sd.Bars[barIdx].Volume,
                                effectiveEquity,
                                customExit,
                                effectiveRisk,
                                capRatio,
                                entryDefinition!.Name
                            );
                        }
                        else if (!isNextOpen)
                        {
                            openPositions[symbol] = new TradeSimulator.OpenPosition
                            {
                                PatternType           = detector.PatternType,
                                CustomPatternName     = (detector as RuleBasedDetector)?.Definition.Name,
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
                                HighestHighSinceEntry = signal.EntryPrice,
                                LowestLowSinceEntry   = signal.EntryPrice,
                                RiskDistance          = stopDistance,
                                EquityAtEntry         = effectiveEquity,
                                CustomExitProfile     = customExit
                            };

                            if (strategyRuntime != null)
                            {
                                strategyRuntime.DailyEntryCount++;
                                strategyRuntime.LastEntryDate = tradingDay;
                            }
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{Symbol} 패턴 {Pattern} 감지 실패", symbol, detector.PatternType);
                    }
                }
            }

            RecordMarkedEquity(date);
        }

        // ── 잔여 포지션 종가 청산 ──
        var finalTradeStart = trades.Count;
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
        ApplyNewTradeCosts(finalTradeStart);
        if (trades.Count > 0)
            RecordMarkedEquity(trades.Max(trade => trade.ExitTime));

        return BacktestResultBuilder.Build(new BacktestResultInputs
        {
            Symbols = symbols,
            Trades = trades,
            RegimeByDate = regimeByDate,
            EquityCurve = equityCurve,
            Warnings = warnings,
            From = from,
            To = to,
            TimeFrame = timeFrame,
            InitialCapital = initialCapital,
            CurrentEquity = currentEquity,
            MaxDrawdown = maxDrawdown,
            TotalSlippage = executionCosts.TotalSlippage,
            TotalCommission = executionCosts.TotalCommission,
            WeightStrategyApplied = weightStrategy != null,
            WeightReducedTrades = weightReducedCount,
            ActualDataFrom = actualDataFrom
        });
    }

    /// <summary>재진입 쿨다운 등록</summary>
    private static void RegisterCooldown(string symbol, int currentBarIndex, TradeRecord lastTrade,
        ReentryConfig? config, Dictionary<string, int> cooldowns)
    {
        if (config == null) return;
        var isLoss = lastTrade.PnL < 0;
        var bars = isLoss ? config.CooldownBarsAfterLoss : config.CooldownBarsAfterWin;
        if (bars > 0)
            cooldowns[symbol] = currentBarIndex + bars + 1;
    }

    /// <summary>서킷브레이커 상태 업데이트</summary>
    private static void UpdateCircuitBreaker(TradeRecord trade, ref int consecutiveLosses,
        ref int circuitBreakerUntilStep, int currentTimelineStep, CircuitBreakerConfig? config)
    {
        if (config == null || config.ConsecutiveLossLimit <= 0) return;
        if (trade.PnL < 0)
        {
            consecutiveLosses++;
            if (consecutiveLosses >= config.ConsecutiveLossLimit)
            {
                circuitBreakerUntilStep = currentTimelineStep + config.CooldownBars + 1;
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
    /// [E-2] 두 심볼의 최근 N봉 일간 수익률 Pearson 상관계수 계산.
    /// </summary>
    private static double ComputePearsonCorrelation(
        decimal[] closesA, Dictionary<DateTime, int> idxA,
        decimal[] closesB, Dictionary<DateTime, int> idxB,
        DateTime refDate, int window)
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

    private sealed class CustomStrategyRuntime
    {
        public required RuleBasedDetector Detector { get; init; }
        public required CircuitBreakerConfig CircuitBreaker { get; init; }
        public required ReentryConfig Reentry { get; init; }
        public required PortfolioRulesConfig Portfolio { get; init; }
        public int ConsecutiveLosses;
        public int CircuitBreakerUntilStep;
        public decimal RealizedEquity;
        public decimal PeakEquity;
        public bool CircuitBreakerTripped;
        public int DailyEntryCount;
        public DateOnly LastEntryDate = DateOnly.MinValue;
    }

}

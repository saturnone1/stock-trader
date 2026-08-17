using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
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
        BacktestExecutionAdapter simulator,
        WeightStrategy? weightStrategy,
        CumulativeRsi2Config cumulativeRsi2Config,
        CancellationToken ct)
    {
        // ── Phase 2: 날짜순 포트폴리오 시뮬레이션 ──
        var allDates = BacktestTimeline.Build(symbolDataMap.Values, from);

        var portfolio = new BacktestPortfolioState(initialCapital, from);
        var openPositions = portfolio.OpenPositions;
        var trades = new List<TradeRecord>();
        var pepCache = new Dictionary<PatternType, BacktestExecutionAdapter.PatternExitProfile>();
        var maxTotalPositions = riskParams.MaxTotalPositions;
        var riskPerTrade = riskParams.RiskPerTradePercent;
        var dailyLossLimitPercent = riskParams.DailyLossLimitPercent;
        Dictionary<string, BacktestStrategyRuntime> strategyRuntimes = null!;
        var executionCosts = new BacktestExecutionCostLedger(
            slippageModel, slippagePercent, commissionPerTrade);

        void ApplyNewTradeCosts(int startIndex)
        {
            executionCosts.ApplyNewTrades(trades, startIndex, trade =>
            {
                portfolio.ApplyRealizedTrade(trade);

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
        var pendingEntryProcessor = new BacktestPendingEntryProcessor();
        var maxWindow = BacktestTimeFramePolicy.Get(timeFrame).SimulationWindowBars;

        // ── 커스텀 패턴 고급 기능: 상태 추적 ──
        // 서킷브레이커, 재진입 쿨다운, 스케일링 등에 사용
        var customDetectors = detectors.OfType<RuleBasedDetector>().ToList();
        var customDetectorsByName = customDetectors
            .GroupBy(detector => detector.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        strategyRuntimes = customDetectorsByName.ToDictionary(
            pair => pair.Key,
            pair => new BacktestStrategyRuntime
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
        var positionExitProcessor = new BacktestPositionExitProcessor();
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
            portfolio.UpdateLatestPrices(date, symbolDataMap);
            if (referenceData != null)
            {
                var referenceAsOf = date;
                foreach (var detector in customDetectors)
                    detector.SetReferenceData(referenceData, referenceAsOf);
            }
            var regime = BacktestExecutionAdapter.GetRegimeForDate(tradingDay, regimeByDate);

            portfolio.BeginTradingDay(tradingDay);

            foreach (var runtime in strategyRuntimes.Values)
            {
                if (runtime.LastEntryDate != tradingDay) runtime.DailyEntryCount = 0;
            }

            // 장중 체결 → 종가 규칙 청산 → 분할매매 순서를 전용 처리기가 보존한다.
            positionExitProcessor.Process(new BacktestPositionExitContext(
                date,
                timelineIndex,
                symbolDataMap,
                maxWindow,
                maxTotalPositions,
                cumulativeRsi2Config,
                pepCache,
                exitOverrides,
                portfolio,
                customDetectorsByName,
                strategyRuntimes,
                reentryCooldowns,
                trades,
                simulator,
                ApplyNewTradeCosts));

            // ── 전략별 피크 에퀴티 + 최대낙폭 거래 중단 체크 ──
            var dailyLossLimitReached =
                portfolio.HasReachedDailyLossLimit(dailyLossLimitPercent);

            if (dailyLossLimitReached)
            {
                pendingEntryProcessor.Clear();
            }
            else
            {
                pendingEntryProcessor.Process(new BacktestPendingEntryContext(
                    date,
                    tradingDay,
                    timelineIndex,
                    maxTotalPositions,
                    symbolDataMap,
                    portfolio,
                    strategyRuntimes,
                    reentryCooldowns,
                    trades,
                    simulator,
                    cumulativeRsi2Config,
                    pepCache,
                    exitOverrides,
                    ApplyNewTradeCosts));
            }

            // ── 2b. 새 진입 ──
            if (dailyLossLimitReached)
            {
                portfolio.RecordMarkedEquity(date);
                continue;
            }

            if (openPositions.Count >= maxTotalPositions)
            {
                portfolio.RecordMarkedEquity(date);
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

                        var baseEquity = Math.Max(portfolio.CurrentEquity, initialCapital * 0.10m);
                        var regimeScale = weightStrategy != null && regime != null
                            ? PositionAllocationPolicy.ResolveRegimeScale(regime, weightStrategy)
                            : 1m;
                        var allocation = PositionAllocationPolicy.Apply(
                            baseEquity, regimeScale, signal.AllocationScale);
                        var effectiveEquity = allocation.EffectiveEquity;
                        portfolio.RegisterWeightReductions(allocation.ReductionCount);

                        if (portfolioRules?.MaxCorrelation > 0
                            && PortfolioCorrelationPolicy.ExceedsLimit(
                                symbol,
                                openPositions.Keys,
                                symbolDataMap,
                                date,
                                portfolioRules.MaxCorrelation))
                        {
                            continue;
                        }

                        // 완료된 과거 거래만 사용해 켈리 비율 계산 (미래 거래 누출 방지)
                        var sizingTrades = ruleDetector == null
                            ? Array.Empty<PositionSizingTradeSample>()
                            : trades
                                .Where(trade => string.Equals(
                                    trade.CustomPatternName,
                                    ruleDetector.Definition.Name,
                                    StringComparison.OrdinalIgnoreCase))
                                .Select(trade => new PositionSizingTradeSample(
                                    trade.PnL, trade.PnLPercent))
                                .ToArray();
                        var effectiveRisk = LongPositionSizingPolicy.ResolveRiskFraction(
                            riskPerTrade,
                            ruleDetector?.Definition.SizingMode,
                            sizingTrades);
                        var sizing = LongPositionSizingPolicy.Calculate(new LongPositionSizingRequest(
                            effectiveEquity,
                            effectiveRisk,
                            signal.EntryPrice,
                            signal.StopLossPrice,
                            effectiveMaxPos,
                            portfolioRules?.MaxSinglePositionPercent ?? 0m));
                        if (!sizing.CanEnter) continue;

                        var quantity = sizing.Quantity;
                        var capRatio = sizing.PositionCapFraction;

                        var entryAtr = sd.Atr[barIdx] > 0 ? sd.Atr[barIdx] : stopDistance;

                        BacktestExecutionAdapter.PatternExitProfile? customExit = null;
                        if (ruleDetector != null)
                        {
                            var def = ruleDetector.Definition;
                            customExit = new BacktestExecutionAdapter.PatternExitProfile(
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

                        var entryDefinition = ruleDetector?.Definition;
                        var isNextOpen = entryDefinition?.EntryMode == StrategyCatalog.NextOpenEntryMode;

                        if (isNextOpen && !pendingEntryProcessor.Contains(symbol))
                        {
                            signal.PendingEntry = true;
                            pendingEntryProcessor.TryAdd(symbol, new BacktestPendingEntry(
                                detector.PatternType,
                                entryDefinition!.Name,
                                signal.EntryPrice,
                                signal.StopLossPrice,
                                signal.TargetPrice,
                                entryAtr,
                                sd.Bars[barIdx].Volume,
                                effectiveEquity,
                                effectiveRisk,
                                capRatio,
                                customExit));
                        }
                        else if (!isNextOpen)
                        {
                            openPositions[symbol] = BacktestOpenPositionFactory.CreateCurrentClose(
                                signal,
                                detector.PatternType,
                                entryDefinition?.Name,
                                sd.Bars[barIdx],
                                barIdx,
                                quantity,
                                entryAtr,
                                effectiveEquity,
                                customExit);

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

            portfolio.RecordMarkedEquity(date);
        }

        // ── 잔여 포지션 종가 청산 ──
        var finalTradeStart = trades.Count;
        foreach (var (symbol, pos) in openPositions)
        {
            if (symbolDataMap.TryGetValue(symbol, out var sd) && sd.Bars.Length > 0)
            {
                var lastBar = sd.Bars[^1];
                var exitQty = pos.CurrentQuantity > 0 ? pos.CurrentQuantity : pos.Quantity;
                trades.Add(BacktestExecutionAdapter.CreateTradeRecord(
                    symbol, pos, lastBar.Close, lastBar.Timestamp, "기간 종료", exitQty));
            }
        }
        ApplyNewTradeCosts(finalTradeStart);
        if (trades.Count > 0)
            portfolio.RecordMarkedEquity(trades.Max(trade => trade.ExitTime));

        return BacktestResultBuilder.Build(new BacktestResultInputs
        {
            Symbols = symbols,
            Trades = trades,
            RegimeByDate = regimeByDate,
            EquityCurve = portfolio.EquityCurve,
            Warnings = warnings,
            From = from,
            To = to,
            TimeFrame = timeFrame,
            InitialCapital = initialCapital,
            CurrentEquity = portfolio.CurrentEquity,
            MaxDrawdown = portfolio.MaxDrawdown,
            TotalSlippage = executionCosts.TotalSlippage,
            TotalCommission = executionCosts.TotalCommission,
            WeightStrategyApplied = weightStrategy != null,
            WeightReducedTrades = portfolio.WeightReducedTrades,
            ActualDataFrom = actualDataFrom
        });
    }

}

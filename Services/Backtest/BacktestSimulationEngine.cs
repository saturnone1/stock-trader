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
    private readonly BacktestSignalEntryProcessor _signalEntryProcessor;

    public BacktestSimulationEngine(BacktestSignalEntryProcessor signalEntryProcessor)
    {
        _signalEntryProcessor = signalEntryProcessor;
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
        var exitPolicyCache = new Dictionary<PatternType, LongPositionExitPolicy>();
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
                exitPolicyCache,
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
                    exitPolicyCache,
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

            await _signalEntryProcessor.ProcessAsync(new BacktestSignalEntryContext(
                date,
                tradingDay,
                timelineIndex,
                initialCapital,
                riskPerTrade,
                maxTotalPositions,
                maxWindow,
                symbols,
                symbolDataMap,
                detectors,
                regime!,
                weightStrategy,
                portfolio,
                strategyRuntimes,
                reentryCooldowns,
                trades,
                pendingEntryProcessor), ct);

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

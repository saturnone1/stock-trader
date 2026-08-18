using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Domain.Backtesting;
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
        var exitPolicyCache = new Dictionary<PatternType, LongPositionExitPolicy>();
        var maxTotalPositions = riskParams.MaxTotalPositions;
        var riskPerTrade = riskParams.RiskPerTradePercent;
        var dailyLossLimitPercent = riskParams.DailyLossLimitPercent;
        var runtimeRegistry = new BacktestStrategyRuntimeRegistry(
            detectors, symbolDataMap, initialCapital);
        var tradeLedger = new BacktestTradeLedger(
            portfolio,
            runtimeRegistry,
            slippageModel,
            slippagePercent,
            commissionPerTrade);
        var pendingEntryProcessor = new BacktestPendingEntryProcessor();
        var maxWindow = BacktestTimeFramePolicy.Get(timeFrame).SimulationWindowBars;

        var positionExitProcessor = new BacktestPositionExitProcessor();

        for (var timelineIndex = 0; timelineIndex < allDates.Count; timelineIndex++)
        {
            var date = allDates[timelineIndex];
            var tradingDay = DateOnly.FromDateTime(date);
            ct.ThrowIfCancellationRequested();
            portfolio.UpdateLatestPrices(date, symbolDataMap);
            runtimeRegistry.BeginStep(date, tradingDay);
            var regime = BacktestExecutionAdapter.GetRegimeForDate(tradingDay, regimeByDate);

            portfolio.BeginTradingDay(tradingDay);

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
                runtimeRegistry,
                tradeLedger,
                simulator));

            // ── 일일 포트폴리오 손실 제한 ──
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
                    maxWindow,
                    symbolDataMap,
                    portfolio,
                    runtimeRegistry,
                    tradeLedger,
                    simulator,
                    cumulativeRsi2Config,
                    exitPolicyCache,
                    exitOverrides));
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
                runtimeRegistry,
                tradeLedger.Trades,
                pendingEntryProcessor), ct);

            portfolio.RecordMarkedEquity(date);
        }

        BacktestTerminalPositionLiquidator.Liquidate(
            symbolDataMap, portfolio, tradeLedger);

        return BacktestResultBuilder.Build(new BacktestResultInputs
        {
            Symbols = symbols,
            Trades = tradeLedger.Trades,
            RegimeByDate = regimeByDate,
            EquityCurve = portfolio.EquityCurve,
            Warnings = warnings,
            From = from,
            To = to,
            InitialCapital = initialCapital,
            CurrentEquity = portfolio.CurrentEquity,
            MaxDrawdown = portfolio.MaxDrawdown,
            TotalSlippage = tradeLedger.TotalSlippage,
            TotalCommission = tradeLedger.TotalCommission,
            WeightStrategyApplied = weightStrategy != null,
            WeightReducedTrades = portfolio.WeightReducedTrades,
            ActualDataFrom = actualDataFrom
        });
    }

}

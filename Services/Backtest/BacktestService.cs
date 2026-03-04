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

        var activeDetectors = BuildDetectors(request.Patterns, request.ParameterOverrides);
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
            request.SlippageModel, ct);

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

                var dateToIndex = new Dictionary<DateOnly, int>();
                for (int i = 0; i < barsArray.Length; i++)
                    dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                symbolDataMap[symbol] = new SymbolPreparedData(barsArray, atrArray, closesArray, sma200Array, dateToIndex);

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
            warnings, actualDataFrom, simulator, ct);
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
        // currentEquity: 실현된 거래 PnL 누적 → 복리 포지션 사이징에 사용
        // 미실현 포지션 가치 제외 (보수적 접근)
        var currentEquity = initialCapital;
        var maxWindow = timeFrame switch
        {
            TimeFrame.OneMinute     => 800,
            TimeFrame.FiveMinute    => 800,
            TimeFrame.FifteenMinute => 600,
            _                       => 260
        };

        foreach (var date in allDates)
        {
            ct.ThrowIfCancellationRequested();
            var regime = TradeSimulator.GetRegimeForDate(date, regimeByDate);

            // ── 2a. 보유 중인 모든 포지션의 청산 로직 ──
            foreach (var symbol in openPositions.Keys.ToList())
            {
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.DateToIndex.TryGetValue(date, out var barIdx)) continue;

                var tradesBefore = trades.Count;
                var exitResult = simulator.ProcessExitLogic(
                    openPositions[symbol], sd.Bars[barIdx], barIdx,
                    sd.Atr[barIdx], sd.Sma200[barIdx],
                    pepCache, exitOverrides, symbol, trades);

                // 청산된 경우 실현 PnL을 currentEquity에 즉시 반영 (복리 사이징용)
                if (exitResult == null)
                {
                    // 청산된 거래의 PnL을 currentEquity에 즉시 반영 (복리 사이징용)
                    for (int ti = tradesBefore; ti < trades.Count; ti++)
                        currentEquity += trades[ti].PnL;
                    openPositions.Remove(symbol);
                }
                else
                    openPositions[symbol] = exitResult;
            }

            // ── 2b. 새 진입 (포트폴리오 포지션 한도 내에서만) ──
            if (openPositions.Count >= maxTotalPositions) continue;

            foreach (var symbol in symbols)
            {
                if (openPositions.ContainsKey(symbol)) continue;
                if (openPositions.Count >= maxTotalPositions) break;
                if (!symbolDataMap.TryGetValue(symbol, out var sd)) continue;
                if (!sd.DateToIndex.TryGetValue(date, out var barIdx)) continue;
                if (barIdx < TradeSimulator.MinWarmupBars) continue;

                var windowSize = Math.Min(barIdx + 1, maxWindow);
                var windowStart = barIdx + 1 - windowSize;
                var windowBars = sd.Bars[windowStart..(barIdx + 1)];

                foreach (var detector in detectors)
                {
                    try
                    {
                        var signal = await detector.DetectAsync(symbol, windowBars, regime, ct);
                        if (signal == null) continue;
                        if (signal.EntryPrice <= 0 || signal.StopLossPrice <= 0) continue;

                        var stopDistance = Math.Abs(signal.EntryPrice - signal.StopLossPrice);
                        if (stopDistance <= 0) continue;

                        // currentEquity 기준 복리 포지션 사이징
                        // 수익 누적 시 점차 큰 포지션, 손실 시 자동 축소 (Kelly 원칙)
                        // 최소 초기자본 10%: 연속 손실로 equity가 지나치게 작아지는 것 방지
                        var effectiveEquity = Math.Max(currentEquity, initialCapital * 0.10m);
                        var riskAmount = effectiveEquity * riskPerTrade;
                        var quantity = (int)(riskAmount / stopDistance);
                        if (quantity <= 0) quantity = 1;

                        var maxPositionCapitalRatio = maxTotalPositions > 0
                            ? 1.0m / maxTotalPositions : 0.10m;
                        var maxQty = (int)(effectiveEquity * maxPositionCapitalRatio / signal.EntryPrice);
                        if (maxQty > 0) quantity = Math.Min(quantity, maxQty);

                        var entryAtr = sd.Atr[barIdx] > 0 ? sd.Atr[barIdx] : stopDistance;

                        openPositions[symbol] = new TradeSimulator.OpenPosition
                        {
                            PatternType           = detector.PatternType,
                            EntryPrice            = signal.EntryPrice,
                            OriginalStop          = signal.StopLossPrice,
                            StopLoss              = signal.StopLossPrice,
                            Target                = signal.TargetPrice,
                            Quantity              = quantity,
                            EntryTime             = sd.Bars[barIdx].Timestamp,
                            EntryBarIndex         = barIdx,
                            EntryAtr              = entryAtr,
                            EntryVolume           = sd.Bars[barIdx].Volume,
                            HighestHighSinceEntry = sd.Bars[barIdx].High,
                            RiskDistance           = stopDistance
                        };

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
                trades.Add(TradeSimulator.CreateTradeRecord(
                    symbol, pos, lastBar.Close, lastBar.Timestamp, "기간 종료", pos.Quantity));
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

                var liquidityFactor = 1.0m;
                if (trade.EntryVolume > 0)
                {
                    var orderRatio = (decimal)trade.Quantity / trade.EntryVolume;
                    liquidityFactor = Math.Max(0.5m, Math.Min(3.0m, 1.0m + (orderRatio - 0.01m) * 50m));
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

        return new BacktestResult
        {
            Trades = trades,
            TotalReturn = totalReturn,
            TotalReturnPercent = totalReturnPct,
            MaxDrawdown = maxDrawdown,
            SharpeRatio = PerformanceCalculator.ComputeSharpeRatio(trades, timeFrame),
            TotalTrades = trades.Count,
            OverallWinRate = overallWinRate,
            PerPatternStats = PerformanceCalculator.ComputePerPatternStats(trades),
            PerSymbolStats = PerformanceCalculator.ComputePerSymbolStats(trades, initialCapital),
            EquityCurve = equityCurve,
            TotalSlippageCost = totalSlippage,
            TotalCommissionCost = totalCommission,
            Warnings = warnings,
            ActualDataFrom = actualDataFrom
        };
    }

    /// <summary>
    /// Walk-Forward 전용 오버로드: 이미 로드된 symbolDataMap에서 날짜 범위를 슬라이싱하여
    /// API 호출 없이 시뮬레이션을 실행합니다.
    /// </summary>
    private async Task<BacktestResult> RunCoreWithPreloadedDataAsync(
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
        CancellationToken ct)
    {
        var simulator = new TradeSimulator(_indicators, _logger);
        var warnings = new List<string>();
        DateTime? actualDataFrom = null;

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

            if (barsSlice.Length < TradeSimulator.MinWarmupBars)
            {
                warnings.Add($"{symbol}: 데이터 부족 ({barsSlice.Length}개)");
                continue;
            }

            // 슬라이싱된 범위에 맞는 dateToIndex 재구성
            var dateToIndex = new Dictionary<DateOnly, int>(barsSlice.Length);
            for (int i = 0; i < barsSlice.Length; i++)
                dateToIndex[DateOnly.FromDateTime(barsSlice[i].Timestamp)] = i;

            symbolDataMap[symbol] = new SymbolPreparedData(barsSlice, atrSlice, closesSlice, sma200Slice, dateToIndex);

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
            warnings, actualDataFrom, simulator, ct);
    }

    /// <summary>심볼별 사전 계산 데이터</summary>
    internal sealed record SymbolPreparedData(
        OhlcvBar[] Bars,
        decimal[] Atr,
        decimal[] Closes,
        decimal[] Sma200,
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

                var dateToIndex = new Dictionary<DateOnly, int>(barsArray.Length);
                for (int i = 0; i < barsArray.Length; i++)
                    dateToIndex[DateOnly.FromDateTime(barsArray[i].Timestamp)] = i;

                wfFullDataMap[symbol] = new SymbolPreparedData(barsArray, atrArray, closesArray, sma200Array, dateToIndex);
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
                request.SlippageModel, ct);

            var oosResult = await RunCoreWithPreloadedDataAsync(
                request.Symbols, wfFullDataMap, detectors, regimeByDate,
                oosFrom, oosTo, request.InitialCapital,
                request.SlippagePercent, request.CommissionPerTrade,
                request.TimeFrame, riskParams, request.ParameterOverrides,
                request.SlippageModel, ct);

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

    /// <summary>백테스트 실행 시 사용할 리스크 파라미터 묶음</summary>
    internal sealed record RiskParams(
        decimal RiskPerTradePercent,
        decimal DailyLossLimitPercent,
        int MaxTotalPositions,
        int MaxPositionsPerSector
    );

    private List<IPatternDetector> BuildDetectors(List<PatternType> patterns, PatternParameterOverrides? overrides)
    {
        if (overrides == null)
            return _detectors.Where(d => patterns.Contains(d.PatternType)).ToList();

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
            new VolatilityBreakoutDetector(_indicators, opts),
            new Tqqq200SmaDetector(_indicators, opts, _settingsRepo)
        };
        return allDetectors.Where(d => patterns.Contains(d.PatternType)).ToList();
    }

    #endregion

    /// <summary>IOptionsSnapshot 래퍼. BacktestService에서 수동 생성한 detector에 전달용.</summary>
    private sealed class OptionsSnapshotWrapper<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public T Value { get; }
        public OptionsSnapshotWrapper(T value) => Value = value;
        public T Get(string? name) => Value;
    }
}

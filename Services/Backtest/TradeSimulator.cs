using StockTrader.Configuration;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 개별 종목에 대한 바-바이-바 시뮬레이션을 수행합니다.
/// 진입 시그널 감지, 포지션 관리(트레일링/부분익절/손절), 청산 로직을 담당합니다.
/// </summary>
internal sealed class TradeSimulator
{

    private readonly IIndicatorService _indicators;
    private readonly ILogger _logger;

    public TradeSimulator(IIndicatorService indicators, ILogger logger)
    {
        _indicators = indicators;
        _logger = logger;
    }

    /// <returns>
    /// (trades, warningMessage, actualDataFrom)
    ///   - warningMessage: null이면 정상, 문자열이면 사용자에게 표시할 경고
    ///   - actualDataFrom: 분봉 기간 클램핑이 발생한 경우 실제 데이터 시작일
    /// </returns>
    public async Task<(List<TradeRecord> trades, string? warning, DateTime? actualDataFrom)> SimulateSymbolAsync(
        string symbol,
        List<OhlcvBar> bars,
        List<IPatternDetector> detectors,
        Dictionary<DateOnly, MarketRegime> regimeByDate,
        DateTime from,
        decimal capital,
        TimeFrame timeFrame,
        BacktestRiskParameters riskParams,
        PatternParameterOverrides? exitOverrides,
        CancellationToken ct)
    {
        if (bars.Count < BacktestDataPolicy.MinimumWarmupBars)
        {
            string warning;
            if (TimeFrameCatalog.IsIntraday(timeFrame))
            {
                var limitDays = timeFrame == TimeFrame.OneMinute ? 7 : 60;
                warning = $"{symbol}: 분봉 데이터 부족 ({bars.Count}개). " +
                          $"Yahoo Finance {GetTimeFrameLabel(timeFrame)} 데이터는 최근 {limitDays}일만 제공됩니다. " +
                          $"시작일을 오늘 기준 {limitDays}일 이내로 설정하세요.";
            }
            else
            {
                warning = $"{symbol}: 데이터 부족 ({bars.Count}개, 최소 {BacktestDataPolicy.MinimumWarmupBars}개 필요). " +
                          "기간을 늘리거나 다른 종목을 선택하세요.";
            }
            _logger.LogWarning("{Symbol}: 데이터 부족 ({Count}개, timeFrame={TimeFrame})", symbol, bars.Count, timeFrame);
            return ([], warning, null);
        }

        var actualDataFrom = bars.Count > 0 ? (DateTime?)bars[0].Timestamp : null;

        var barsArray = bars.ToArray();
        var atrArray = _indicators.ATR(barsArray, 14);
        var closesArray = IndicatorService.ExtractCloses(barsArray);
        var sma200Array = _indicators.SMA(closesArray, 200);
        var cumulativeRsi2Config = new CumulativeRsi2Config();
        var cumulativeRsi2Array = _indicators.CumulativeRsi(
            closesArray, cumulativeRsi2Config.RsiPeriod, cumulativeRsi2Config.CumulativePeriod);
        var cumulativeRsi2TrendMaArray = _indicators.SMA(
            closesArray, cumulativeRsi2Config.LongTrendMaPeriod);

        var pepCache = new Dictionary<PatternType, PatternExitProfile>();
        var trades = new List<TradeRecord>();
        OpenPosition? openPosition = null;

        var riskPerTrade = riskParams.RiskPerTradePercent;
        var maxTotalPositions = riskParams.MaxTotalPositions;

        for (int i = BacktestDataPolicy.MinimumWarmupBars; i < bars.Count; i++)
        {
            var currentBar = bars[i];
            if (currentBar.Timestamp < from) continue;

            ct.ThrowIfCancellationRequested();

            var currentDate = DateOnly.FromDateTime(currentBar.Timestamp);
            var regime = GetRegimeForDate(currentDate, regimeByDate);

            // ── Exit logic ──
            if (openPosition != null)
            {
                var exitResult = ProcessExitLogic(
                    openPosition, currentBar, i, atrArray[i], sma200Array[i],
                    cumulativeRsi2Array[i], cumulativeRsi2TrendMaArray[i], cumulativeRsi2Config,
                    pepCache, exitOverrides, symbol, trades);
                openPosition = exitResult;
            }

            if (openPosition != null) continue;

            // ── Entry logic ──
            var maxWindow = BacktestTimeFramePolicy.Get(timeFrame).SimulationWindowBars;
            var windowSize = Math.Min(i + 1, maxWindow);
            var windowStart = i + 1 - windowSize;
            var windowBars = barsArray[windowStart..(i + 1)];

            foreach (var detector in detectors)
            {
                try
                {
                    var signal = await detector.DetectAsync(symbol, windowBars, regime, ct);
                    if (signal == null) continue;
                    if (signal.EntryPrice <= 0 || signal.StopLossPrice <= 0) continue;

                    var stopDistance = Math.Abs(signal.EntryPrice - signal.StopLossPrice);
                    if (stopDistance <= 0) continue;

                    int quantity;
                    if (detector.PatternType == PatternType.Tqqq200Sma)
                    {
                        quantity = (int)(capital * 0.95m / signal.EntryPrice);
                        if (quantity <= 0) quantity = 1;
                    }
                    else
                    {
                        var riskAmount = capital * riskPerTrade;
                        quantity = (int)(riskAmount / stopDistance);
                        if (quantity <= 0) quantity = 1;

                        var maxPositionCapitalRatio = maxTotalPositions > 0
                            ? 1.0m / maxTotalPositions
                            : 0.10m;
                        var maxQty = (int)(capital * maxPositionCapitalRatio / signal.EntryPrice);
                        if (maxQty > 0) quantity = Math.Min(quantity, maxQty);
                    }

                    var entryAtr = atrArray[i] > 0 ? atrArray[i] : stopDistance;

                    openPosition = new OpenPosition
                    {
                        PatternType           = detector.PatternType,
                        EntryPrice            = signal.EntryPrice,
                        OriginalStop          = signal.StopLossPrice,
                        StopLoss              = signal.StopLossPrice,
                        Target                = signal.TargetPrice,
                        Quantity              = quantity,
                        EntryTime             = currentBar.Timestamp,
                        EntryBarIndex         = i,
                        EntryAtr              = entryAtr,
                        EntryVolume           = currentBar.Volume,
                        // CurrentClose 진입은 해당 봉이 닫힌 뒤 체결된다.
                        // 진입 전에 발생한 동일 고가/저가를 MFE/MAE에 포함하지 않는다.
                        HighestHighSinceEntry = signal.EntryPrice,
                        LowestLowSinceEntry   = signal.EntryPrice,
                        RiskDistance           = stopDistance
                    };

                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Symbol} 패턴 {Pattern} 감지 실패",
                        symbol, detector.PatternType);
                }
            }
        }

        // Close remaining position at last bar's close
        if (openPosition != null && bars.Count > 0)
        {
            var lastBar = bars[^1];
            trades.Add(CreateTradeRecord(symbol, openPosition, lastBar.Close,
                lastBar.Timestamp, "기간 종료", openPosition.Quantity));
        }

        _logger.LogInformation("{Symbol}: {Count}건 거래 완료", symbol, trades.Count);
        return (trades, null, actualDataFrom);
    }

    /// <summary>
    /// 오픈 포지션의 청산 로직을 처리합니다.
    /// 청산되면 null 반환, 유지되면 (갱신된) openPosition 반환.
    /// </summary>
    internal OpenPosition? ProcessExitLogic(
        OpenPosition openPosition,
        OhlcvBar currentBar,
        int barIndex,
        decimal currentAtrRaw,
        decimal sma200,
        decimal currentCumulativeRsi2,
        decimal currentCumulativeRsi2TrendMa,
        CumulativeRsi2Config cumulativeRsi2Config,
        Dictionary<PatternType, PatternExitProfile> pepCache,
        PatternParameterOverrides? exitOverrides,
        string symbol,
        List<TradeRecord> trades)
    {
        var currentAtr = currentAtrRaw > 0 ? currentAtrRaw : openPosition.EntryAtr;

        PatternExitProfile pep;
        if (openPosition.CustomExitProfile != null)
        {
            pep = openPosition.CustomExitProfile;
        }
        else if (!pepCache.TryGetValue(openPosition.PatternType, out pep!))
        {
            pep = PatternExitProfile.For(openPosition.PatternType, exitOverrides);
            pepCache[openPosition.PatternType] = pep;
        }

        StrategyExitInstruction? strategyExit = null;
        if (openPosition.PatternType == PatternType.CumulativeRsi2
            && currentCumulativeRsi2TrendMa > 0
            && currentBar.Close <= currentCumulativeRsi2TrendMa)
        {
            strategyExit = new StrategyExitInstruction(
                currentBar.Close,
                $"{cumulativeRsi2Config.LongTrendMaPeriod}SMA 이탈");
        }
        else if (openPosition.PatternType == PatternType.CumulativeRsi2
                 && currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold)
        {
            strategyExit = new StrategyExitInstruction(
                currentBar.Close,
                $"누적 RSI 청산({currentCumulativeRsi2:F1})");
        }

        var state = new LongPositionExecutionState(
            openPosition.EntryPrice,
            openPosition.StopLoss,
            openPosition.Target,
            openPosition.HighestHighSinceEntry,
            openPosition.LowestLowSinceEntry,
            openPosition.RiskDistance,
            openPosition.EntryAtr,
            openPosition.EntryBarIndex,
            openPosition.CurrentQuantity > 0 ? openPosition.CurrentQuantity : openPosition.Quantity,
            openPosition.PartialProfitTaken,
            openPosition.BreakevenApplied,
            openPosition.TrailingStopActivated);
        var tqqqSmaExit = openPosition.PatternType == PatternType.Tqqq200Sma;
        var stopReason = tqqqSmaExit ? "SMA200 이탈" : "손절";
        var policy = new LongPositionExitPolicy(
            pep.MaxHoldingBars,
            pep.EnableTrailingStop,
            pep.TrailingStopAtrMultiplier,
            pep.TrailingActivationR,
            pep.EnablePartialProfit,
            pep.PartialProfitRMultiple,
            pep.EnableTargetExit,
            pep.EnableTimeExit,
            pep.BreakevenAtrMultiplier,
            StopReason: stopReason,
            ProtectedStopReason: tqqqSmaExit ? stopReason : "트레일링 손절");
        var result = LongPositionExecutionPolicy.Evaluate(
            state,
            currentBar,
            barIndex,
            currentAtr,
            policy,
            strategyExit,
            tqqqSmaExit && sma200 > 0 ? sma200 * 0.99m : null);

        var positionForFill = openPosition;
        foreach (var executionEvent in result.Events)
        {
            if (executionEvent.Type == PositionExecutionEventType.StopMoved)
                continue;

            trades.Add(CreateTradeRecord(
                symbol,
                positionForFill,
                executionEvent.Price,
                currentBar.Timestamp,
                executionEvent.Reason,
                executionEvent.Quantity));

            if (executionEvent.Type == PositionExecutionEventType.PartialExit)
                positionForFill = CopyWithExecutionState(openPosition, result.State, quantityBecomesRemaining: true);
        }

        if (result.IsClosed)
            return null;

        return CopyWithExecutionState(
            openPosition,
            result.State,
            result.Events.Any(item => item.Type == PositionExecutionEventType.PartialExit));
    }

    private static OpenPosition CopyWithExecutionState(
        OpenPosition source,
        LongPositionExecutionState state,
        bool quantityBecomesRemaining)
    {
        return new OpenPosition
        {
            PatternType = source.PatternType,
            CustomPatternName = source.CustomPatternName,
            EntryPrice = source.EntryPrice,
            OriginalStop = source.OriginalStop,
            StopLoss = state.StopPrice,
            Target = source.Target,
            Quantity = quantityBecomesRemaining ? state.CurrentQuantity : source.Quantity,
            CurrentQuantity = state.CurrentQuantity,
            TotalCost = quantityBecomesRemaining ? source.EntryPrice * state.CurrentQuantity : source.TotalCost,
            EntryTime = source.EntryTime,
            EntryBarIndex = source.EntryBarIndex,
            EntryAtr = source.EntryAtr,
            EntryVolume = source.EntryVolume,
            HighestHighSinceEntry = state.HighestPrice,
            LowestLowSinceEntry = state.LowestPrice,
            TrailingStopActivated = state.TrailingActivated,
            BreakevenApplied = state.BreakevenApplied,
            PartialProfitTaken = state.PartialProfitTaken,
            RiskDistance = source.RiskDistance,
            EquityAtEntry = source.EquityAtEntry,
            CustomExitProfile = source.CustomExitProfile,
            ScaleCounts = source.ScaleCounts,
        };
    }

    internal static TradeRecord CreateTradeRecord(
        string symbol, OpenPosition pos, decimal exitPrice,
        DateTime exitTime, string exitReason, int qty)
    {
        var pnl = (exitPrice - pos.EntryPrice) * qty;
        var pnlPct = pos.EntryPrice > 0
            ? (exitPrice - pos.EntryPrice) / pos.EntryPrice
            : 0;

        // [B-3] MAE/MFE 계산
        var maePercent = pos.EntryPrice > 0 && pos.LowestLowSinceEntry > 0
            ? (pos.LowestLowSinceEntry - pos.EntryPrice) / pos.EntryPrice * 100
            : 0;
        var mfePercent = pos.EntryPrice > 0 && pos.HighestHighSinceEntry > 0
            ? (pos.HighestHighSinceEntry - pos.EntryPrice) / pos.EntryPrice * 100
            : 0;

        return new TradeRecord
        {
            Symbol         = symbol,
            PatternType    = pos.PatternType,
            CustomPatternName = pos.CustomPatternName,
            EntryPrice     = pos.EntryPrice,
            ExitPrice      = exitPrice,
            Quantity       = qty,
            EntryTime      = pos.EntryTime,
            ExitTime       = exitTime,
            PnL            = pnl,
            PnLPercent     = pnlPct,
            ExitReason     = exitReason,
            EntryAtr       = pos.EntryAtr,
            EntryVolume    = pos.EntryVolume,
            EquityAtEntry  = pos.EquityAtEntry,
            MaePercent     = maePercent,
            MfePercent     = mfePercent
        };
    }

    internal static string GetTimeFrameLabel(TimeFrame tf) => TimeFrameCatalog.DisplayName(tf);

    internal static MarketRegime GetRegimeForDate(
        DateOnly date, Dictionary<DateOnly, MarketRegime> regimeByDate)
    {
        if (regimeByDate.TryGetValue(date, out var regime))
            return regime;

        var closest = regimeByDate
            .Where(kv => kv.Key <= date)
            .OrderByDescending(kv => kv.Key)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        return closest ?? new MarketRegime
        {
            SpyAbove200Ma = true,
            RegimeLabel = "알 수 없음"
        };
    }

    /// <summary>오픈 포지션 상태 추적 (심볼별 1개)</summary>
    internal sealed class OpenPosition
    {
        public PatternType PatternType { get; init; }
        public decimal EntryPrice { get; set; }
        public string? CustomPatternName { get; init; }
        public decimal OriginalStop { get; init; }
        public decimal Target { get; init; }
        public int Quantity { get; init; }
        public DateTime EntryTime { get; init; }
        public int EntryBarIndex { get; init; }
        public decimal EntryAtr { get; init; }
        public long EntryVolume { get; init; }

        public decimal StopLoss { get; set; }
        public decimal HighestHighSinceEntry { get; set; }
        /// <summary>진입 이후 최저 저가 — MAE 계산에 사용</summary>
        public decimal LowestLowSinceEntry { get; set; }
        public bool TrailingStopActivated { get; set; }
        public bool BreakevenApplied { get; set; }
        public bool PartialProfitTaken { get; set; }
        public decimal RiskDistance { get; init; }
        /// <summary>진입 시점의 포트폴리오 자본 — EquityAtEntry 계산에 사용</summary>
        public decimal EquityAtEntry { get; init; }
        /// <summary>커스텀 패턴용 청산 프로파일. null이면 PatternType 기반 기본값 사용.</summary>
        public PatternExitProfile? CustomExitProfile { get; init; }

        // ── 스케일링 추적 ──
        /// <summary>현재 수량 (스케일인/아웃으로 변동 가능)</summary>
        public int CurrentQuantity { get; set; }
        /// <summary>스케일링 규칙별 실행 횟수</summary>
        public Dictionary<int, int>? ScaleCounts { get; set; }
        /// <summary>총 투자금 (가중 평균가 계산용)</summary>
        public decimal TotalCost { get; set; }
    }

    /// <summary>
    /// 패턴별 청산 프로파일. 보유 기간, 트레일링, 부분익절 등을 패턴 특성에 맞게 설정합니다.
    /// </summary>
    internal sealed record PatternExitProfile(
        int MaxHoldingBars,
        bool EnableTrailingStop,
        decimal TrailingStopAtrMultiplier,
        decimal TrailingActivationR,
        bool EnablePartialProfit,
        decimal PartialProfitRMultiple,
        bool EnableTargetExit,
        bool EnableTimeExit,
        decimal BreakevenAtrMultiplier = 1.5m)
    {
        public static PatternExitProfile For(PatternType pt, PatternParameterOverrides? ov = null)
        {
            var baseline = GetBaseline(pt);
            if (ov == null) return baseline;

            var (maxBars, trailAtr, partialR) = pt switch
            {
                PatternType.GapUpPullback           => (ov.GapUp_ExitMaxHoldingBars,    ov.GapUp_ExitTrailingAtr,    ov.GapUp_ExitPartialR),
                PatternType.Breakout                => (ov.Breakout_ExitMaxHoldingBars,  ov.Breakout_ExitTrailingAtr, ov.Breakout_ExitPartialR),
                PatternType.VwapReversion           => (ov.Vwap_ExitMaxHoldingBars,      ov.Vwap_ExitTrailingAtr,     ov.Vwap_ExitPartialR),
                PatternType.RsiMeanReversion        => (ov.Rsi_ExitMaxHoldingBars,       ov.Rsi_ExitTrailingAtr,      ov.Rsi_ExitPartialR),
                PatternType.TrendPullback           => (ov.Trend_ExitMaxHoldingBars,     ov.Trend_ExitTrailingAtr,    ov.Trend_ExitPartialR),
                PatternType.OpeningRangeBreakout    => (ov.Orb_ExitMaxHoldingBars,       ov.Orb_ExitTrailingAtr,      ov.Orb_ExitPartialR),
                PatternType.VolumeSpikeContinuation => (ov.VolSpike_ExitMaxHoldingBars,  ov.VolSpike_ExitTrailingAtr, ov.VolSpike_ExitPartialR),
                PatternType.EarningsDrift           => (ov.Earnings_ExitMaxHoldingBars,  ov.Earnings_ExitTrailingAtr, ov.Earnings_ExitPartialR),
                PatternType.IndexRegimeFilter       => (ov.Regime_ExitMaxHoldingBars,    ov.Regime_ExitTrailingAtr,   ov.Regime_ExitPartialR),
                PatternType.VolatilityExpansion     => (ov.Vola_ExitMaxHoldingBars,      ov.Vola_ExitTrailingAtr,     ov.Vola_ExitPartialR),
                PatternType.MomentumReversal        => (ov.Mom_ExitMaxHoldingBars,       ov.Mom_ExitTrailingAtr,      ov.Mom_ExitPartialR),
                PatternType.MultiTimeframeTrend     => (ov.Mtf_ExitMaxHoldingBars,       ov.Mtf_ExitTrailingAtr,      ov.Mtf_ExitPartialR),
                PatternType.MeanReversionChannel    => (ov.Chan_ExitMaxHoldingBars,      ov.Chan_ExitTrailingAtr,     ov.Chan_ExitPartialR),
                PatternType.Rsi2Bollinger           => (ov.Rsi2Bb_ExitMaxHoldingBars,    ov.Rsi2Bb_ExitTrailingAtr,   ov.Rsi2Bb_ExitPartialR),
                PatternType.CumulativeRsi2          => (ov.CumRsi2_ExitMaxHoldingBars,   ov.CumRsi2_ExitTrailingAtr,  ov.CumRsi2_ExitPartialR),
                PatternType.VolatilityBreakout      => (ov.VolBrk_ExitMaxHoldingBars,    ov.VolBrk_ExitTrailingAtr,   ov.VolBrk_ExitPartialR),
                PatternType.Tqqq200Sma              => (ov.Tqqq_ExitMaxHoldingBars,      (decimal?)null,              (decimal?)null),
                _                                   => ((int?)null, (decimal?)null, (decimal?)null)
            };

            if (maxBars == null && trailAtr == null && partialR == null)
                return baseline;

            return baseline with
            {
                MaxHoldingBars = maxBars ?? baseline.MaxHoldingBars,
                EnableTrailingStop = trailAtr.HasValue ? trailAtr.Value > 0 : baseline.EnableTrailingStop,
                TrailingStopAtrMultiplier = trailAtr ?? baseline.TrailingStopAtrMultiplier,
                EnablePartialProfit = partialR.HasValue ? partialR.Value > 0 : baseline.EnablePartialProfit,
                PartialProfitRMultiple = partialR ?? baseline.PartialProfitRMultiple
            };
        }

        private static PatternExitProfile GetBaseline(PatternType pt) => pt switch
        {
            // ── Day Trading ──
            PatternType.GapUpPullback           => new( 3, false, 0m,   0m,   true,  2.0m, true,  true),
            PatternType.VwapReversion           => new( 3, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.OpeningRangeBreakout    => new( 3, false, 0m,   0m,   true,  2.0m, true,  true),
            PatternType.VolumeSpikeContinuation => new( 5, true,  1.5m, 1.0m, false, 0m,   true,  true),
            PatternType.VolatilityBreakout      => new( 5, true,  2.0m, 1.0m, false, 0m,   true,  true),

            // ── Mean Reversion ──
            PatternType.RsiMeanReversion        => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.VolatilityExpansion     => new( 7, true,  2.0m, 1.5m, true,  2.0m, true,  true),
            PatternType.MeanReversionChannel    => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.Rsi2Bollinger           => new( 5, false, 0m,   0m,   true,  1.5m, true,  true),
            PatternType.CumulativeRsi2          => new(20, false, 0m,   0m,   false, 0m,   false, false, 0m),

            // ── Swing Trading ──
            PatternType.Breakout                => new(15, true,  2.5m, 1.5m, true,  2.5m, true,  true),
            PatternType.MomentumReversal        => new(10, true,  2.5m, 1.5m, true,  2.0m, true,  true),
            PatternType.IndexRegimeFilter       => new(15, true,  2.5m, 1.5m, true,  2.0m, true,  true),

            // ── Position/Trend ──
            PatternType.TrendPullback           => new(20, true,  3.0m, 2.0m, true,  3.0m, true,  true),
            PatternType.EarningsDrift           => new(20, true,  2.5m, 1.5m, true,  2.0m, true,  true),
            PatternType.MultiTimeframeTrend     => new(30, true,  3.0m, 2.0m, true,  3.0m, true,  true),

            // ── Regime (SMA200 이탈까지 무제한) ──
            PatternType.Tqqq200Sma              => new(999, false, 0m,  0m,   false, 0m,   false, false),

            _ => new(20, true, 2.5m, 1.0m, true, 2.0m, true, true)
        };
    }
}

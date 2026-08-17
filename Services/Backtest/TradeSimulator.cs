using StockTrader.Configuration;
using StockTrader.Application.Backtesting;
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
        BacktestService.RiskParams riskParams,
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
        var barsSinceEntry = barIndex - openPosition.EntryBarIndex;

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

        // OHLC만으로는 한 봉 안에서 고가와 저가 중 무엇이 먼저였는지 알 수 없다.
        // 따라서 봉 시작 전에 확정되어 있던 손절가를 가장 먼저 확인한다.
        // 현재 봉에서 새로 계산한 손익분기/트레일링 스탑은 다음 봉부터 효력을 갖는다.
        var stopAtBarOpen = openPosition.StopLoss;
        if (currentBar.Low <= stopAtBarOpen)
        {
            var stopFill = currentBar.Open > 0 && currentBar.Open < stopAtBarOpen
                ? currentBar.Open
                : stopAtBarOpen;
            openPosition.LowestLowSinceEntry = openPosition.LowestLowSinceEntry == 0
                ? stopFill
                : Math.Min(openPosition.LowestLowSinceEntry, stopFill);
            var stopQty = openPosition.CurrentQuantity > 0
                ? openPosition.CurrentQuantity : openPosition.Quantity;
            var stopReason = openPosition.PatternType == PatternType.Tqqq200Sma
                ? "SMA200 이탈"
                : openPosition.BreakevenApplied || openPosition.TrailingStopActivated
                    ? "트레일링 손절"
                    : "손절";
            trades.Add(CreateTradeRecord(symbol, openPosition, stopFill,
                currentBar.Timestamp, stopReason, stopQty));
            return null;
        }

        // 손절에 걸리지 않은 봉만 전체 고가/저가를 보유 중 익스커션으로 반영한다.
        if (currentBar.High > openPosition.HighestHighSinceEntry)
            openPosition.HighestHighSinceEntry = currentBar.High;
        if (openPosition.LowestLowSinceEntry == 0 || currentBar.Low < openPosition.LowestLowSinceEntry)
            openPosition.LowestLowSinceEntry = currentBar.Low;

        // 부분 익절. 이 봉에서 손절가 동시에 닿았다면 위의 보수적 손절이 우선한다.
        if (pep.EnablePartialProfit && !openPosition.PartialProfitTaken)
        {
            var partialProfitTarget = openPosition.EntryPrice
                + openPosition.RiskDistance * pep.PartialProfitRMultiple;
            var effectiveQty = openPosition.CurrentQuantity > 0
                ? openPosition.CurrentQuantity : openPosition.Quantity;
            if (currentBar.High >= partialProfitTarget && effectiveQty >= 2)
            {
                var halfQty = effectiveQty / 2;
                var remainQty = effectiveQty - halfQty;

                trades.Add(CreateTradeRecord(
                    symbol, openPosition, partialProfitTarget,
                    currentBar.Timestamp, $"부분 익절({pep.PartialProfitRMultiple}R)", halfQty));

                openPosition = new OpenPosition
                {
                    PatternType              = openPosition.PatternType,
                    CustomPatternName        = openPosition.CustomPatternName,
                    EntryPrice               = openPosition.EntryPrice,
                    OriginalStop             = openPosition.OriginalStop,
                    StopLoss                 = Math.Max(openPosition.StopLoss, openPosition.EntryPrice),
                    Target                   = openPosition.Target,
                    Quantity                 = remainQty,
                    CurrentQuantity          = remainQty,
                    TotalCost                = openPosition.EntryPrice * remainQty,
                    EntryTime                = openPosition.EntryTime,
                    EntryBarIndex            = openPosition.EntryBarIndex,
                    EntryAtr                 = openPosition.EntryAtr,
                    EntryVolume              = openPosition.EntryVolume,
                    HighestHighSinceEntry    = openPosition.HighestHighSinceEntry,
                    LowestLowSinceEntry      = openPosition.LowestLowSinceEntry,
                    TrailingStopActivated    = openPosition.TrailingStopActivated,
                    BreakevenApplied         = true,
                    PartialProfitTaken       = true,
                    RiskDistance              = openPosition.RiskDistance,
                    CustomExitProfile        = openPosition.CustomExitProfile,
                    ScaleCounts              = openPosition.ScaleCounts
                };
            }
        }

        // 손절을 통과한 뒤 목표가/종가 청산을 평가한다.
        decimal exitPrice = 0;
        string exitReason = "";

        if (pep.EnableTargetExit && currentBar.High >= openPosition.Target)
        {
            exitPrice = openPosition.Target;
            exitReason = "목표 도달";
        }
        else if (openPosition.PatternType == PatternType.CumulativeRsi2
                 && currentCumulativeRsi2TrendMa > 0
                 && currentBar.Close <= currentCumulativeRsi2TrendMa)
        {
            exitPrice = currentBar.Close;
            exitReason = $"{cumulativeRsi2Config.LongTrendMaPeriod}SMA 이탈";
        }
        else if (openPosition.PatternType == PatternType.CumulativeRsi2
                 && currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold)
        {
            exitPrice = currentBar.Close;
            exitReason = $"누적 RSI 청산({currentCumulativeRsi2:F1})";
        }
        else if (pep.EnableTimeExit && barsSinceEntry >= pep.MaxHoldingBars)
        {
            exitPrice = currentBar.Close;
            exitReason = $"시간 청산({pep.MaxHoldingBars}봉)";
        }

        if (exitPrice > 0)
        {
            // 스케일링된 수량 반영 (CurrentQuantity > 0이면 스케일링이 적용된 것)
            var exitQty = openPosition.CurrentQuantity > 0
                ? openPosition.CurrentQuantity : openPosition.Quantity;
            trades.Add(CreateTradeRecord(symbol, openPosition, exitPrice,
                currentBar.Timestamp, exitReason, exitQty));
            return null;
        }

        // 종가로 확정한 보호 스탑은 다음 봉부터 사용한다.
        if (!openPosition.BreakevenApplied && openPosition.EntryAtr > 0
            && pep.BreakevenAtrMultiplier > 0)
        {
            var breakevenThreshold = openPosition.EntryPrice + openPosition.EntryAtr * pep.BreakevenAtrMultiplier;
            if (currentBar.Close >= breakevenThreshold)
            {
                openPosition.StopLoss = Math.Max(openPosition.StopLoss, openPosition.EntryPrice);
                openPosition.BreakevenApplied = true;
            }
        }

        if (pep.EnableTrailingStop)
        {
            var activationTarget = openPosition.EntryPrice
                + openPosition.RiskDistance * pep.TrailingActivationR;
            if (!openPosition.TrailingStopActivated && currentBar.Close >= activationTarget)
                openPosition.TrailingStopActivated = true;

            if (openPosition.TrailingStopActivated && currentAtr > 0)
            {
                var chandelier = openPosition.HighestHighSinceEntry
                    - currentAtr * pep.TrailingStopAtrMultiplier;
                if (chandelier > openPosition.StopLoss)
                    openPosition.StopLoss = chandelier;
            }
        }

        if (openPosition.PatternType == PatternType.Tqqq200Sma && sma200 > 0)
        {
            var dynamicSmaStop = sma200 * 0.99m;
            if (dynamicSmaStop > openPosition.StopLoss)
                openPosition.StopLoss = dynamicSmaStop;
        }

        return openPosition;
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

using StockTrader.Configuration;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 공유 장기 포지션 체결 정책을 백테스트 거래 기록과 연결합니다.
/// </summary>
internal sealed class BacktestExecutionAdapter
{

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
        Dictionary<PatternType, LongPositionExitPolicy> exitPolicyCache,
        PatternParameterOverrides? exitOverrides,
        string symbol,
        List<TradeRecord> trades)
    {
        var currentAtr = currentAtrRaw > 0 ? currentAtrRaw : openPosition.EntryAtr;

        LongPositionExitPolicy policy;
        if (openPosition.CustomExitProfile != null)
        {
            policy = openPosition.CustomExitProfile;
        }
        else if (!exitPolicyCache.TryGetValue(openPosition.PatternType, out policy!))
        {
            policy = LongPositionExitPolicyCatalog.ForPattern(openPosition.PatternType, exitOverrides);
            exitPolicyCache[openPosition.PatternType] = policy;
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
        policy = policy with
        {
            StopReason = stopReason,
            ProtectedStopReason = tqqqSmaExit ? stopReason : "트레일링 손절"
        };
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
        public LongPositionExitPolicy? CustomExitProfile { get; init; }

        // ── 스케일링 추적 ──
        /// <summary>현재 수량 (스케일인/아웃으로 변동 가능)</summary>
        public int CurrentQuantity { get; set; }
        /// <summary>스케일링 규칙별 실행 횟수</summary>
        public Dictionary<int, int>? ScaleCounts { get; set; }
        /// <summary>총 투자금 (가중 평균가 계산용)</summary>
        public decimal TotalCost { get; set; }
    }

}

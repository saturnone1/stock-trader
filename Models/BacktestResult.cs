using StockTrader.Models.Enums;

using StockTrader.Application.Strategies;
using StockTrader.Domain.Backtesting;

namespace StockTrader.Models;

/// <summary>
/// 백테스트 실행 시 패턴별 파라미터를 일시적으로 오버라이드합니다.
/// 라이브 트레이딩 설정(appsettings.json)은 변경하지 않습니다.
/// 키는 파라미터 이름(C# 프로퍼티명), 값은 decimal 또는 int 또는 string.
/// </summary>
public class PatternParameterOverrides
{
    // GapUpPullback
    public decimal? GapUp_MinGapPercent { get; set; }
    public decimal? GapUp_MaxPullbackPercent { get; set; }
    public long? GapUp_MinVolume { get; set; }

    // Breakout
    public int? Breakout_LookbackDays { get; set; }
    public decimal? Breakout_MinVolumeMultiplier { get; set; }
    public decimal? Breakout_BreakoutMarginPercent { get; set; }
    public decimal? Breakout_AtrStopMultiplier { get; set; }
    public decimal? Breakout_AtrTargetMultiplier { get; set; }

    // VwapReversion
    public decimal? Vwap_MaxDeviationPercent { get; set; }
    public decimal? Vwap_MinBouncePercent { get; set; }
    public decimal? Vwap_MinBounceFromLowPercent { get; set; }

    // RsiMeanReversion
    public int? Rsi_OversoldThreshold { get; set; }
    public int? Rsi_Period { get; set; }
    public decimal? Rsi_MinVolumeIncreaseMultiplier { get; set; }
    public decimal? Rsi_AtrStopMultiplier { get; set; }
    public decimal? Rsi_AtrTargetMultiplier { get; set; }

    // TrendPullback
    public int? Trend_MaPeriod { get; set; }
    public decimal? Trend_MaxPullbackFromMa { get; set; }
    public int? Trend_TrendConfirmationDays { get; set; }
    public decimal? Trend_AtrStopMultiplier { get; set; }
    public decimal? Trend_AtrTargetMultiplier { get; set; }

    // OpeningRangeBreakout
    public int? Orb_RangeMinutes { get; set; }
    public decimal? Orb_MinRangePercent { get; set; }

    // VolumeSpikeContinuation
    public decimal? VolSpike_VolumeMultiplier { get; set; }
    public int? VolSpike_ContinuationBars { get; set; }
    public int? VolSpike_VolumeAvgPeriod { get; set; }
    public decimal? VolSpike_AtrStopMultiplier { get; set; }
    public decimal? VolSpike_AtrTargetMultiplier { get; set; }

    // EarningsDrift
    public int? Earnings_DriftDays { get; set; }
    public decimal? Earnings_MinSurprisePercent { get; set; }

    // IndexRegimeFilter
    public int? Regime_MaPeriod { get; set; }
    public string? Regime_IndexSymbol { get; set; }

    // VolatilityExpansion
    public int? Vola_BollingerPeriod { get; set; }
    public decimal? Vola_StdDevMultiplier { get; set; }
    public decimal? Vola_AtrStopMultiplier { get; set; }
    public decimal? Vola_AtrTargetMultiplier { get; set; }

    // MomentumReversal
    public int? Mom_FastEmaPeriod { get; set; }
    public int? Mom_SlowEmaPeriod { get; set; }
    public int? Mom_MacdSignalPeriod { get; set; }
    public int? Mom_RsiPeriod { get; set; }
    public int? Mom_RsiOversold { get; set; }
    public int? Mom_RsiOverbought { get; set; }
    public int? Mom_RsiMomentumMin { get; set; }
    public decimal? Mom_AtrStopMultiplier { get; set; }
    public decimal? Mom_AtrTargetMultiplier { get; set; }

    // MultiTimeframeTrend
    public int? Mtf_LongTrendMaPeriod { get; set; }
    public int? Mtf_ShortEntryMaPeriod { get; set; }
    public decimal? Mtf_MaxPullbackPercent { get; set; }
    public int? Mtf_TrendConfirmationBars { get; set; }
    public decimal? Mtf_MaxDistanceAboveShortMa { get; set; }
    public decimal? Mtf_AtrStopMultiplier { get; set; }
    public decimal? Mtf_AtrTargetMultiplier { get; set; }

    // MeanReversionChannel
    public int? Chan_EmaPeriod { get; set; }
    public int? Chan_AtrPeriod { get; set; }
    public decimal? Chan_AtrMultiplier { get; set; }
    public int? Chan_RsiPeriod { get; set; }
    public int? Chan_RsiOversold { get; set; }
    public int? Chan_RecentLowLookbackBars { get; set; }

    // Rsi2Bollinger
    public int? Rsi2Bb_RsiPeriod { get; set; }
    public decimal? Rsi2Bb_RsiThreshold { get; set; }
    public int? Rsi2Bb_BollingerPeriod { get; set; }
    public decimal? Rsi2Bb_BollingerStdDev { get; set; }
    public int? Rsi2Bb_LongTrendMaPeriod { get; set; }
    public decimal? Rsi2Bb_AtrStopMultiplier { get; set; }

    // CumulativeRsi2
    public int? CumRsi2_RsiPeriod { get; set; }
    public int? CumRsi2_CumulativePeriod { get; set; }
    public decimal? CumRsi2_EntryThreshold { get; set; }
    public decimal? CumRsi2_ExitThreshold { get; set; }
    public int? CumRsi2_LongTrendMaPeriod { get; set; }
    public int? CumRsi2_ExitSmaPeriod { get; set; }
    public decimal? CumRsi2_AtrStopMultiplier { get; set; }
    public decimal? CumRsi2_PlaceholderTargetAtrMultiplier { get; set; }

    // VolatilityBreakout
    public decimal? VolBrk_BreakoutFactor { get; set; }
    public decimal? VolBrk_MinVolumeMultiplier { get; set; }
    public int? VolBrk_VolumeAvgPeriod { get; set; }
    public decimal? VolBrk_AtrStopMultiplier { get; set; }
    public decimal? VolBrk_AtrTargetMultiplier { get; set; }

    // Tqqq200Sma
    public int? Tqqq_SmaPeriod { get; set; }
    public decimal? Tqqq_EntryDistancePercent { get; set; }
    public decimal? Tqqq_FixedStopPercent { get; set; }
    public decimal? Tqqq_SmaStopMultiplier { get; set; }
    public decimal? Tqqq_TargetSmaMultiplier { get; set; }
    public decimal? Tqqq_MinimumTargetReturnPercent { get; set; }
    public decimal? Tqqq_SpyExitDistancePercent { get; set; }
    public decimal? Tqqq_MaxVolatility20d { get; set; }
    public decimal? Tqqq_OverheatStage1 { get; set; }
    public decimal? Tqqq_OverheatStage2 { get; set; }
    public decimal? Tqqq_OverheatPercent { get; set; }
    public int? Tqqq_ConfirmationDays { get; set; }
    public int? Tqqq_ShortTrendEmaPeriod { get; set; }
    public int? Tqqq_VolumeAvgPeriod { get; set; }
    public decimal? Tqqq_MinVolumeRatio { get; set; }
    public decimal? Tqqq_AtrStopMultiplier { get; set; }
    public decimal? Tqqq_AtrTargetMultiplier { get; set; }

    // ── 패턴별 청산 전략 오버라이드 ─────────────────────────────────────
    // MaxHoldingBars: 최대 보유 봉 수
    // TrailingAtr: 트레일링 스톱 ATR 배수 (0 = 비활성)
    // PartialR: 부분 익절 R배수 (0 = 비활성)

    // GapUpPullback Exit
    public int? GapUp_ExitMaxHoldingBars { get; set; }
    public decimal? GapUp_ExitTrailingAtr { get; set; }
    public decimal? GapUp_ExitPartialR { get; set; }

    // Breakout Exit
    public int? Breakout_ExitMaxHoldingBars { get; set; }
    public decimal? Breakout_ExitTrailingAtr { get; set; }
    public decimal? Breakout_ExitPartialR { get; set; }

    // VwapReversion Exit
    public int? Vwap_ExitMaxHoldingBars { get; set; }
    public decimal? Vwap_ExitTrailingAtr { get; set; }
    public decimal? Vwap_ExitPartialR { get; set; }

    // RsiMeanReversion Exit
    public int? Rsi_ExitMaxHoldingBars { get; set; }
    public decimal? Rsi_ExitTrailingAtr { get; set; }
    public decimal? Rsi_ExitPartialR { get; set; }

    // TrendPullback Exit
    public int? Trend_ExitMaxHoldingBars { get; set; }
    public decimal? Trend_ExitTrailingAtr { get; set; }
    public decimal? Trend_ExitPartialR { get; set; }

    // OpeningRangeBreakout Exit
    public int? Orb_ExitMaxHoldingBars { get; set; }
    public decimal? Orb_ExitTrailingAtr { get; set; }
    public decimal? Orb_ExitPartialR { get; set; }

    // VolumeSpikeContinuation Exit
    public int? VolSpike_ExitMaxHoldingBars { get; set; }
    public decimal? VolSpike_ExitTrailingAtr { get; set; }
    public decimal? VolSpike_ExitPartialR { get; set; }

    // EarningsDrift Exit
    public int? Earnings_ExitMaxHoldingBars { get; set; }
    public decimal? Earnings_ExitTrailingAtr { get; set; }
    public decimal? Earnings_ExitPartialR { get; set; }

    // IndexRegimeFilter Exit
    public int? Regime_ExitMaxHoldingBars { get; set; }
    public decimal? Regime_ExitTrailingAtr { get; set; }
    public decimal? Regime_ExitPartialR { get; set; }

    // VolatilityExpansion Exit
    public int? Vola_ExitMaxHoldingBars { get; set; }
    public decimal? Vola_ExitTrailingAtr { get; set; }
    public decimal? Vola_ExitPartialR { get; set; }

    // MomentumReversal Exit
    public int? Mom_ExitMaxHoldingBars { get; set; }
    public decimal? Mom_ExitTrailingAtr { get; set; }
    public decimal? Mom_ExitPartialR { get; set; }

    // MultiTimeframeTrend Exit
    public int? Mtf_ExitMaxHoldingBars { get; set; }
    public decimal? Mtf_ExitTrailingAtr { get; set; }
    public decimal? Mtf_ExitPartialR { get; set; }

    // MeanReversionChannel Exit
    public int? Chan_ExitMaxHoldingBars { get; set; }
    public decimal? Chan_ExitTrailingAtr { get; set; }
    public decimal? Chan_ExitPartialR { get; set; }

    // Rsi2Bollinger Exit
    public int? Rsi2Bb_ExitMaxHoldingBars { get; set; }
    public decimal? Rsi2Bb_ExitTrailingAtr { get; set; }
    public decimal? Rsi2Bb_ExitPartialR { get; set; }

    // CumulativeRsi2 Exit
    public int? CumRsi2_ExitMaxHoldingBars { get; set; }
    public decimal? CumRsi2_ExitTrailingAtr { get; set; }
    public decimal? CumRsi2_ExitPartialR { get; set; }

    // VolatilityBreakout Exit
    public int? VolBrk_ExitMaxHoldingBars { get; set; }
    public decimal? VolBrk_ExitTrailingAtr { get; set; }
    public decimal? VolBrk_ExitPartialR { get; set; }

    // Tqqq200Sma Exit
    public int? Tqqq_ExitMaxHoldingBars { get; set; }
}

public class BacktestRequest
{
    public List<string> Symbols { get; set; } = new();
    public List<PatternType> Patterns { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal InitialCapital { get; set; } = 100_000m;

    /// <summary>
    /// 백테스트에 사용할 봉 타임프레임. 기본값 Daily (일봉).
    /// 분봉 선택 시 데이터 양이 많아지므로 기간을 짧게 설정하는 것을 권장합니다.
    /// </summary>
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;

    // Slippage & Commission
    public decimal SlippagePercent { get; set; } = 0.05m;    // 0.05% per trade (Fixed 모드용)
    public decimal CommissionPerTrade { get; set; } = 1.00m;  // $1 per trade

    /// <summary>
    /// 슬리피지 모델 선택. Fixed=고정비율, Adaptive=변동성/유동성 반영.
    /// </summary>
    public SlippageModel SlippageModel { get; set; } = BacktestExecutionCatalog.DefaultSlippageModel;

    // Walk-Forward
    public bool EnableWalkForward { get; set; }
    public int WalkForwardInSampleMonths { get; set; } = 12;
    public int WalkForwardOutOfSampleMonths { get; set; } = 3;

    // Monte Carlo
    public bool EnableMonteCarlo { get; set; }
    public int MonteCarloSimulations { get; set; } = 1000;

    // ── 리스크 관리 파라미터 ──────────────────────────────────────────
    /// <summary>1회 매매당 리스크 비율 (0.01 = 1%). null이면 appsettings.json 기본값 사용</summary>
    public decimal? RiskPerTradePercent { get; set; }

    /// <summary>일일 최대 손실 한도 비율 (0.03 = 3%). null이면 appsettings.json 기본값 사용</summary>
    public decimal? DailyLossLimitPercent { get; set; }

    /// <summary>전체 최대 동시 포지션 수. null이면 appsettings.json 기본값 사용</summary>
    public int? MaxTotalPositions { get; set; }

    /// <summary>섹터당 최대 포지션 수. null이면 appsettings.json 기본값 사용</summary>
    public int? MaxPositionsPerSector { get; set; }

    /// <summary>
    /// 백테스트 전용 패턴 파라미터 오버라이드. null이면 appsettings.json 기본값을 사용합니다.
    /// </summary>
    public PatternParameterOverrides? ParameterOverrides { get; set; }

    /// <summary>
    /// 데이터 소스 지정. null이면 UserSettings의 PreferredDataSource 사용.
    /// </summary>
    public DataSource? DataSource { get; set; }

    /// <summary>
    /// 포트폴리오 레벨 비중 관리 전략. null이면 비중 조절 없이 기존 방식으로 작동.
    /// 활성화 시 시장 레짐(SPY vs 200SMA)에 따라 포지션 크기를 자동 조절합니다.
    /// </summary>
    public WeightStrategy? WeightStrategy { get; set; }

    /// <summary>백테스트 모드. 현재는 "pattern"만 사용합니다.</summary>
    public string BacktestMode { get; set; } = "pattern";

    /// <summary>
    /// 커스텀 패턴 정의 목록. DB에 저장된 ID 또는 인라인 정의를 전달합니다.
    /// Patterns에 Custom이 포함되어 있으면 이 목록의 패턴들이 사용됩니다.
    /// </summary>
    public List<StrategyDocument>? CustomPatterns { get; set; }
}

/// <summary>
/// 포트폴리오 레벨 비중 관리 전략 파라미터.
/// SPY(또는 지정 지수)의 200일 이동평균 대비 위치에 따라 포지션 크기를 조절합니다.
/// </summary>
public class WeightStrategy
{
    /// <summary>강세장(지수 > SMA) 비중 배수. 기본 1.0 (100%)</summary>
    public decimal BullWeight { get; set; } = 1.0m;

    /// <summary>약세장(지수 &lt; SMA) 비중 배수. 기본 0.3 (30%)</summary>
    public decimal BearWeight { get; set; } = 0.3m;

    /// <summary>과열1단계 비중 배수 (지수가 SMA 대비 OverheatStage1Pct 이상). 기본 0.7</summary>
    public decimal Overheat1Weight { get; set; } = 0.7m;

    /// <summary>과열2단계 비중 배수 (지수가 SMA 대비 OverheatStage2Pct 이상). 기본 0.4</summary>
    public decimal Overheat2Weight { get; set; } = 0.4m;

    /// <summary>과열1단계 이격도 임계값. 기본 1.15 (115%)</summary>
    public decimal OverheatStage1Pct { get; set; } = 1.15m;

    /// <summary>과열2단계 이격도 임계값. 기본 1.25 (125%)</summary>
    public decimal OverheatStage2Pct { get; set; } = 1.25m;

    /// <summary>레짐 판단 SMA 기간. 기본 200</summary>
    public int SmaPeriod { get; set; } = 200;
}

public class BacktestResult
{
    public List<TradeRecord> Trades { get; set; } = new();
    public decimal TotalReturn { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public int TotalTrades { get; set; }
    public decimal OverallWinRate { get; set; }
    public Dictionary<PatternType, PatternStats> PerPatternStats { get; set; } = new();
    public Dictionary<string, PatternStats> PerStrategyStats { get; set; } = new();
    public List<SymbolStats> PerSymbolStats { get; set; } = new();

    /// <summary>백테스트에 사용된 타임프레임 (결과 표시용)</summary>
    public TimeFrame UsedTimeFrame { get; set; } = TimeFrame.Daily;

    // Equity curve data points (date → equity value)
    public List<EquityPoint> EquityCurve { get; set; } = new();

    // Slippage/Commission totals
    public decimal TotalSlippageCost { get; set; }
    public decimal TotalCommissionCost { get; set; }

    // Walk-Forward results
    public WalkForwardResult? WalkForward { get; set; }

    // Monte Carlo results
    public MonteCarloResult? MonteCarlo { get; set; }

    /// <summary>비중 전략 적용 여부</summary>
    public bool WeightStrategyApplied { get; set; }

    /// <summary>비중 전략에 의해 축소된 거래 수</summary>
    public int WeightReducedTrades { get; set; }

    /// <summary>
    /// 치명적 오류 메시지. null이 아니면 백테스트 전체가 실패한 것을 의미합니다.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 비치명적 경고 목록. 데이터 기간 조정, 종목별 데이터 부족 등의 상황을 알립니다.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>실제 데이터 조회에 사용된 시작일 (분봉 기간 제한으로 조정된 경우 요청일과 다를 수 있음)</summary>
    public DateTime? ActualDataFrom { get; set; }

    // 성과 지표 확장
    public decimal SortinoRatio { get; set; }
    public decimal CalmarRatio { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AnnualizedReturn { get; set; }

    // Kelly Criterion
    public decimal KellyFraction { get; set; }
    public decimal HalfKellyFraction { get; set; }

    // 생존자 편향 경고
    public string? SurvivorshipBiasWarning { get; set; }

    // 레짐별 성과
    public Dictionary<string, RegimePerformance>? PerRegimeStats { get; set; }

    // MAE/MFE 통계
    public decimal AvgMaePercent { get; set; }
    public decimal AvgMfePercent { get; set; }
    public decimal MedianMaePercent { get; set; }
    public decimal MedianMfePercent { get; set; }
}

public class RegimePerformance
{
    public int TradeCount { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal AvgReturnPercent { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
}

/// <summary>종목별 백테스트 성과 요약</summary>
public class SymbolStats
{
    public string Symbol { get; set; } = "";
    public int TradeCount { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal AvgPnLPercent { get; set; }
    /// <summary>최대 1회 투입 금액 (진입가 × 수량)</summary>
    public decimal MaxPositionSize { get; set; }
    /// <summary>자본금 대비 최대 배분 비율</summary>
    public decimal MaxAllocationPercent { get; set; }
}

public record EquityPoint(DateTime Date, decimal Equity);

public class WalkForwardResult
{
    public List<WalkForwardWindow> Windows { get; set; } = new();
    public decimal AggregateOosReturn { get; set; }
    public decimal AggregateOosReturnPercent { get; set; }
    public decimal AggregateOosMaxDrawdown { get; set; }
    public decimal AggregateOosWinRate { get; set; }
    public decimal AggregateOosSharpe { get; set; }
    public decimal WalkForwardEfficiency { get; set; }  // OOS return / IS return
}

public class WalkForwardWindow
{
    public DateTime InSampleFrom { get; set; }
    public DateTime InSampleTo { get; set; }
    public DateTime OutOfSampleFrom { get; set; }
    public DateTime OutOfSampleTo { get; set; }
    public int InSampleTrades { get; set; }
    public decimal InSampleReturn { get; set; }
    public decimal InSampleReturnPercent { get; set; }
    public int OutOfSampleTrades { get; set; }
    public decimal OutOfSampleReturn { get; set; }
    public decimal OutOfSampleReturnPercent { get; set; }
    public decimal OutOfSampleMaxDrawdown { get; set; }
    public decimal OutOfSampleSharpe { get; set; }
    public decimal Efficiency { get; set; }
}

public class MonteCarloResult
{
    public int Simulations { get; set; }
    public decimal MedianFinalEquity { get; set; }
    public decimal MeanFinalEquity { get; set; }
    public decimal Percentile5Equity { get; set; }
    public decimal Percentile25Equity { get; set; }
    public decimal Percentile75Equity { get; set; }
    public decimal Percentile95Equity { get; set; }
    public decimal MedianMaxDrawdown { get; set; }
    public decimal WorstCaseMaxDrawdown { get; set; } // 95th percentile DD
    public decimal ProbabilityOfLoss { get; set; }
    public List<decimal> EquityDistribution { get; set; } = new(); // sorted final equities
}

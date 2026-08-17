using StockTrader.Models;

namespace StockTrader.Configuration;

public class PatternSettings
{
    public List<PatternType> EnabledPatterns { get; set; } = new()
    {
        PatternType.GapUpPullback,
        PatternType.Breakout,
        PatternType.VwapReversion
    };

    public GapUpPullbackConfig GapUpPullback { get; set; } = new();
    public BreakoutConfig Breakout { get; set; } = new();
    public VwapReversionConfig VwapReversion { get; set; } = new();
    public RsiMeanReversionConfig RsiMeanReversion { get; set; } = new();
    public TrendPullbackConfig TrendPullback { get; set; } = new();
    public OrbConfig OpeningRangeBreakout { get; set; } = new();
    public VolumeSpikeConfig VolumeSpikeContinuation { get; set; } = new();
    public EarningsDriftConfig EarningsDrift { get; set; } = new();
    public IndexRegimeConfig IndexRegimeFilter { get; set; } = new();
    public VolatilityExpansionConfig VolatilityExpansion { get; set; } = new();
    public MomentumReversalConfig MomentumReversal { get; set; } = new();
    public MultiTimeframeTrendConfig MultiTimeframeTrend { get; set; } = new();
    public MeanReversionChannelConfig MeanReversionChannel { get; set; } = new();
    public Rsi2BollingerConfig Rsi2Bollinger { get; set; } = new();
    public VolatilityBreakoutConfig VolatilityBreakout { get; set; } = new();
    public Tqqq200SmaConfig Tqqq200Sma { get; set; } = new();
    public CumulativeRsi2Config CumulativeRsi2 { get; set; } = new();
}

public class GapUpPullbackConfig
{
    public decimal MinGapPercent { get; set; } = 0.005m;
    public decimal MaxPullbackPercent { get; set; } = 0.8m;
    public long MinVolume { get; set; } = 100_000;
}

public class BreakoutConfig
{
    public int LookbackDays { get; set; } = 10;
    public decimal MinVolumeMultiplier { get; set; } = 1.0m;
    public decimal BreakoutMarginPercent { get; set; } = 0.001m;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal AtrTargetMultiplier { get; set; } = 4.0m;
}

public class VwapReversionConfig
{
    public decimal MaxDeviationPercent { get; set; } = 0.01m;
    public decimal MinBouncePercent { get; set; } = 0.003m;
    public decimal MinBounceFromLowPercent { get; set; } = 0.2m;
}

public class RsiMeanReversionConfig
{
    public int OversoldThreshold { get; set; } = 30;
    public int Period { get; set; } = 14;
    public decimal MinVolumeIncreaseMultiplier { get; set; } = 1.0m;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal AtrTargetMultiplier { get; set; } = 2.0m;
}

public class TrendPullbackConfig
{
    public int MaPeriod { get; set; } = 20;
    public decimal MaxPullbackFromMa { get; set; } = 0.025m;
    public int TrendConfirmationDays { get; set; } = 7;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal AtrTargetMultiplier { get; set; } = 5.0m;
}

public class OrbConfig
{
    public int RangeMinutes { get; set; } = 15;
    public decimal MinRangePercent { get; set; } = 0.005m;
}

public class VolumeSpikeConfig
{
    public decimal VolumeMultiplier { get; set; } = 1.5m;
    public int ContinuationBars { get; set; } = 2;
    public int VolumeAvgPeriod { get; set; } = 20;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
}

public class EarningsDriftConfig
{
    public int DriftDays { get; set; } = 5;
    public decimal MinSurprisePercent { get; set; } = 0.05m;
}

public class IndexRegimeConfig
{
    public int MaPeriod { get; set; } = 200;
    public string IndexSymbol { get; set; } = "SPY";
}

public class VolatilityExpansionConfig
{
    public int BollingerPeriod { get; set; } = 20;
    public decimal StdDevMultiplier { get; set; } = 2.0m;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal AtrTargetMultiplier { get; set; } = 2.0m;
}

public class MomentumReversalConfig
{
    public int FastEmaPeriod { get; set; } = 12;
    public int SlowEmaPeriod { get; set; } = 26;
    public int MacdSignalPeriod { get; set; } = 9;
    public int RsiPeriod { get; set; } = 14;
    public int RsiOversold { get; set; } = 30;
    public int RsiOverbought { get; set; } = 70;
    public int RsiMomentumMin { get; set; } = 40;
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
}

public class MultiTimeframeTrendConfig
{
    public int LongTrendMaPeriod { get; set; } = 50;
    public int ShortEntryMaPeriod { get; set; } = 20;
    public decimal MaxPullbackPercent { get; set; } = 0.05m;
    public int TrendConfirmationBars { get; set; } = 3;
    public decimal MaxDistanceAboveShortMa { get; set; } = 0.02m;
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
}

public class MeanReversionChannelConfig
{
    public int EmaPeriod { get; set; } = 25;
    public int AtrPeriod { get; set; } = 10;
    public decimal AtrMultiplier { get; set; } = 1.5m;
    public int RsiPeriod { get; set; } = 14;
    public int RsiOversold { get; set; } = 40;
    public int RecentLowLookbackBars { get; set; } = 5;
}

/// <summary>
/// Connors Research RSI(2) + Bollinger Band mean reversion config.
/// Backtested win rate: 75-82% on S&P 500 (1995-2007).
/// </summary>
public class Rsi2BollingerConfig
{
    /// <summary>RSI period. Connors uses 2 for extreme short-term oversold.</summary>
    public int RsiPeriod { get; set; } = 2;

    /// <summary>RSI threshold below which the stock is considered extremely oversold.</summary>
    public decimal RsiThreshold { get; set; } = 20m;

    /// <summary>Bollinger Band SMA period.</summary>
    public int BollingerPeriod { get; set; } = 20;

    /// <summary>Bollinger Band standard deviation multiplier.</summary>
    public decimal BollingerStdDev { get; set; } = 2.0m;

    /// <summary>Long-term trend MA period. Stock must be above this to qualify.</summary>
    public int LongTrendMaPeriod { get; set; } = 200;

    /// <summary>ATR multiplier for stop loss placement (entry - ATR * multiplier).</summary>
    public decimal AtrStopMultiplier { get; set; } = 1.5m;
}

/// <summary>
/// Connors cumulative RSI(2) mean reversion config.
/// 최근 N일 RSI(2) 합산값으로 극단 과매도/과열을 판단한다.
/// </summary>
public class CumulativeRsi2Config
{
    /// <summary>개별 RSI 계산 기간. Connors 기본값은 2.</summary>
    public int RsiPeriod { get; set; } = 2;

    /// <summary>누적할 RSI 봉 수. 문헌 기본값은 2일 합산.</summary>
    public int CumulativePeriod { get; set; } = 2;

    /// <summary>진입 누적 RSI 임계값. 이 값 이하에서 매수.</summary>
    public decimal EntryThreshold { get; set; } = 10m;

    /// <summary>청산 누적 RSI 임계값. 이 값 이상에서 청산.</summary>
    public decimal ExitThreshold { get; set; } = 65m;

    /// <summary>장기 추세 필터 이동평균 기간. 기본 200일.</summary>
    public int LongTrendMaPeriod { get; set; } = 200;

    /// <summary>브래킷 목표가 보조 계산용 단기 SMA 기간.</summary>
    public int ExitSmaPeriod { get; set; } = 5;

    /// <summary>고정 손절 ATR 배수.</summary>
    public decimal AtrStopMultiplier { get; set; } = 1.5m;

    /// <summary>실거래 브래킷 주문용 보조 목표 ATR 배수. 실제 청산은 누적 RSI/추세 이탈이 우선.</summary>
    public decimal PlaceholderTargetAtrMultiplier { get; set; } = 8.0m;
}

/// <summary>
/// Larry Williams Volatility Breakout (VIX/range-expansion breakout) config.
/// 1987 World Trading Championship: 11,376% return. Win rate 55-65%.
/// </summary>
public class VolatilityBreakoutConfig
{
    /// <summary>
    /// Breakout factor K: entry = Open + (PrevRange * K).
    /// Williams' original value is 0.6.
    /// </summary>
    public decimal BreakoutFactor { get; set; } = 0.5m;

    /// <summary>Minimum volume ratio vs 20-bar average to confirm breakout.</summary>
    public decimal MinVolumeMultiplier { get; set; } = 1.0m;

    /// <summary>Volume average lookback period.</summary>
    public int VolumeAvgPeriod { get; set; } = 20;

    /// <summary>ATR multiplier for stop loss (entry - ATR * multiplier).</summary>
    public decimal AtrStopMultiplier { get; set; } = 2.0m;

    /// <summary>ATR multiplier for target (entry + ATR * multiplier).</summary>
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
}

/// <summary>
/// TQQQ 200-SMA 변형매매법 (fmkorea "200일 티큐 변형매매법" 기반).
///
/// 핵심 규칙:
///   1. 이격도(Close/SMA200×100) >= 101% → 풀매수
///   2. SPY가 200일선 대비 97.75% 이하 → 전량 청산 (진입 차단)
///   3. 20일 수익률 표준편차 >= 5.9% → 전량 청산 (진입 차단)
///   4. 과열 감량: 139%→신뢰도 하락, 146%→진입 차단
///   5. 손절: 진입가 -5.9% 고정 또는 SMA200×0.99 중 높은 쪽
///   6. 목표가: SMA200×1.50 (넓게 설정, BacktestExecutionAdapter 동적 스탑에 위임)
///
/// 백테스트 기준: 추세추종 장기 홀딩, 조건부 청산 방식.
/// </summary>
public class Tqqq200SmaConfig
{
    /// <summary>장기 추세 판단 SMA 기간 (200일).</summary>
    public int SmaPeriod { get; set; } = 200;

    /// <summary>이격도 진입 임계값. Close/SMA200 >= 이 값이면 진입. 1.01 = 101%.</summary>
    public decimal EntryDistancePercent { get; set; } = 1.01m;

    /// <summary>고정 손절 비율. 진입가 × (1 - 이 값). 0.059 = -5.9%.</summary>
    public decimal FixedStopPercent { get; set; } = 0.059m;

    /// <summary>SPY 청산 이격도 임계값. SPY가 200MA 대비 이 비율 이하면 진입 차단. 0.9775 = 97.75%.</summary>
    public decimal SpyExitDistancePercent { get; set; } = 0.9775m;

    /// <summary>20일 일간 수익률 표준편차 상한. 이 값 이상이면 진입 차단. 0.059 = 5.9%.</summary>
    public decimal MaxVolatility20d { get; set; } = 0.059m;

    /// <summary>과열 1단계 이격도. 이 값 이상이면 신뢰도 감소(0.45). 1.39 = 139%.</summary>
    public decimal OverheatStage1 { get; set; } = 1.39m;

    /// <summary>과열 2단계 이격도. 이 값 이상이면 진입 차단. 1.46 = 146%.</summary>
    public decimal OverheatStage2 { get; set; } = 1.46m;

    /// <summary>거래량 평균 기간 (보조 필터).</summary>
    public int VolumeAvgPeriod { get; set; } = 20;

    /// <summary>진입 시 최소 거래량 배수 (20일 평균 대비). 1.0 = 평균 이상.</summary>
    public decimal MinVolumeRatio { get; set; } = 1.0m;

    /// <summary>이 패턴을 적용할 심볼 목록. 비어있으면 TQQQ만 허용.</summary>
    public List<string> AllowedSymbols { get; set; } = new() { "TQQQ" };
}

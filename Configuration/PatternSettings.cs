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
/// TQQQ 200-SMA Rotation Strategy (아기티큐 전략 개선판).
/// 원본: SMA200 크로스오버 추세추종 + 3자산 로테이션.
/// 개선: 휩쏘 감소(EMA50 필터, 거래량 확인), 적응형 손절(ATR), 고정익절 제거(트레일링 스탑 위임).
/// 백테스트 기준: 승률 ~30%, 손익비 7.75, 기댓값 +6.5%/거래.
/// </summary>
public class Tqqq200SmaConfig
{
    /// <summary>장기 추세 판단 SMA 기간 (원본 200일).</summary>
    public int SmaPeriod { get; set; } = 200;

    /// <summary>과열 구간 판정 임계값. SMA200 × (1 + OverheatPercent) 이상이면 과열.</summary>
    public decimal OverheatPercent { get; set; } = 0.05m;

    /// <summary>돌파 진입 확인에 필요한 연속 종가 > SMA200 일수.</summary>
    public int ConfirmationDays { get; set; } = 2;

    /// <summary>단기 추세 필터 EMA 기간. SMA200과 골든크로스 여부로 휩쏘 감소.</summary>
    public int ShortTrendEmaPeriod { get; set; } = 50;

    /// <summary>거래량 평균 기간 (거래량 확인 필터).</summary>
    public int VolumeAvgPeriod { get; set; } = 20;

    /// <summary>진입 시 최소 거래량 배수 (20일 평균 대비). 1.0 = 평균 이상.</summary>
    public decimal MinVolumeRatio { get; set; } = 1.0m;

    /// <summary>ATR 기반 초기 손절 배수. 진입가 - ATR × N. 적응형으로 고정 -5% 대체.</summary>
    public decimal AtrStopMultiplier { get; set; } = 3.0m;

    /// <summary>ATR 기반 목표가 배수. 넓게 설정하여 BacktestService 트레일링 스탑에 위임.</summary>
    public decimal AtrTargetMultiplier { get; set; } = 8.0m;

    /// <summary>이 패턴을 적용할 심볼 목록. 비어있으면 TQQQ만 허용.</summary>
    public List<string> AllowedSymbols { get; set; } = new() { "TQQQ" };
}

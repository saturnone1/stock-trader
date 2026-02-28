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
}

public class GapUpPullbackConfig
{
    public decimal MinGapPercent { get; set; } = 0.02m;
    public decimal MaxPullbackPercent { get; set; } = 0.5m;
    public long MinVolume { get; set; } = 500_000;
}

public class BreakoutConfig
{
    public int LookbackDays { get; set; } = 252;
    public decimal MinVolumeMultiplier { get; set; } = 1.5m;
    // 0.5% margin: current close must exceed 52W high by at least 0.5% to confirm breakout
    public decimal BreakoutMarginPercent { get; set; } = 0.005m;
}

public class VwapReversionConfig
{
    public decimal MaxDeviationPercent { get; set; } = 0.02m;
    public decimal MinBouncePercent { get; set; } = 0.003m;
}

public class RsiMeanReversionConfig
{
    public int OversoldThreshold { get; set; } = 30;
    public int Period { get; set; } = 14;
}

public class TrendPullbackConfig
{
    public int MaPeriod { get; set; } = 20;
    public decimal MaxPullbackFromMa { get; set; } = 0.02m;
    public int TrendConfirmationDays { get; set; } = 10;
}

public class OrbConfig
{
    public int RangeMinutes { get; set; } = 15;
    public decimal MinRangePercent { get; set; } = 0.005m;
}

public class VolumeSpikeConfig
{
    public decimal VolumeMultiplier { get; set; } = 2.0m;
    public int ContinuationBars { get; set; } = 3;
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
}

public class MomentumReversalConfig
{
    public int FastEmaPeriod { get; set; } = 12;
    public int SlowEmaPeriod { get; set; } = 26;
    public int MacdSignalPeriod { get; set; } = 9;
    public int RsiPeriod { get; set; } = 14;
    public int RsiOversold { get; set; } = 30;
    public int RsiOverbought { get; set; } = 70;
}

public class MultiTimeframeTrendConfig
{
    public int LongTrendMaPeriod { get; set; } = 50;
    public int ShortEntryMaPeriod { get; set; } = 20;
    public decimal MaxPullbackPercent { get; set; } = 0.03m;
    public int TrendConfirmationBars { get; set; } = 5;
}

public class MeanReversionChannelConfig
{
    public int EmaPeriod { get; set; } = 20;
    public int AtrPeriod { get; set; } = 10;
    public decimal AtrMultiplier { get; set; } = 1.5m;
    public int RsiPeriod { get; set; } = 14;
    public int RsiOversold { get; set; } = 35;
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
    public decimal RsiThreshold { get; set; } = 10m;

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
    public decimal BreakoutFactor { get; set; } = 0.6m;

    /// <summary>Minimum volume ratio vs 20-bar average to confirm breakout.</summary>
    public decimal MinVolumeMultiplier { get; set; } = 1.2m;

    /// <summary>Volume average lookback period.</summary>
    public int VolumeAvgPeriod { get; set; } = 20;

    /// <summary>ATR multiplier for stop loss (entry - ATR * multiplier).</summary>
    public decimal AtrStopMultiplier { get; set; } = 2.0m;

    /// <summary>ATR multiplier for target (entry + ATR * multiplier).</summary>
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
}

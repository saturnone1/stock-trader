namespace StockTrader.Models;

public enum PatternType
{
    GapUpPullback = 1,
    Breakout = 2,
    VwapReversion = 3,
    RsiMeanReversion = 4,
    TrendPullback = 5,
    OpeningRangeBreakout = 6,
    VolumeSpikeContinuation = 7,
    EarningsDrift = 8,
    IndexRegimeFilter = 9,
    VolatilityExpansion = 10,
    MomentumReversal = 11,
    MultiTimeframeTrend = 12,
    MeanReversionChannel = 13,
    Rsi2Bollinger = 14,
    VolatilityBreakout = 15,
    Tqqq200Sma = 16,
    CumulativeRsi2 = 17,
    Custom = 100
}

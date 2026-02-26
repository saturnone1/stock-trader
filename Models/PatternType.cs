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
    MeanReversionChannel = 13
}

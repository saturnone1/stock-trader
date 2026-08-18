using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Analysis;

public sealed record StockIndicatorSnapshot(
    IndicatorSnapshot Indicators,
    decimal Atr,
    decimal VolumeRatio);

public sealed record MarketTrendSnapshot(
    decimal Price,
    decimal MovingAverage,
    bool IsAboveMovingAverage);

public sealed class StockIndicatorSnapshotFactory(IIndicatorService indicators)
{
    private const int RsiPeriod = 14;
    private const int AtrPeriod = 14;
    private const int VolumePeriod = 20;
    private const int ShortTrendPeriod = 20;
    private const int MediumTrendPeriod = 50;
    public const int LongTrendPeriod = 200;
    private const int FastEmaPeriod = 12;
    private const int SlowEmaPeriod = 26;
    private const int SignalEmaPeriod = 9;
    private const int BollingerPeriod = 20;
    private const decimal BollingerDeviation = 2m;

    public StockIndicatorSnapshot Create(OhlcvBar[] bars)
    {
        var closes = bars.Select(bar => bar.Close).ToArray();
        var currentPrice = closes[^1];
        var snapshot = new IndicatorSnapshot();
        var bullishCount = 0;
        var totalCount = 0;

        var rsi = indicators.RSI(closes, RsiPeriod);
        snapshot.RSI = rsi.Length > 0 ? rsi[^1] : 50;
        totalCount++;
        if (snapshot.RSI > 30 && snapshot.RSI < 70) bullishCount++;

        AddSma(closes, ShortTrendPeriod, value => snapshot.SMA20 = value);
        AddSma(closes, MediumTrendPeriod, value => snapshot.SMA50 = value);
        AddSma(closes, LongTrendPeriod, value => snapshot.SMA200 = value);

        if (closes.Length >= SlowEmaPeriod)
        {
            var macd = indicators.MACD(closes, FastEmaPeriod, SlowEmaPeriod, SignalEmaPeriod);
            snapshot.MACD = macd.MacdLine[^1];
            snapshot.MACDSignal = macd.SignalLine[^1];
            totalCount++;
            if (snapshot.MACD > snapshot.MACDSignal) bullishCount++;
        }

        if (closes.Length >= BollingerPeriod)
        {
            var bands = indicators.BollingerBands(closes, BollingerPeriod, BollingerDeviation);
            snapshot.BollingerUpper = bands.Upper[^1];
            snapshot.BollingerMiddle = bands.Middle[^1];
            snapshot.BollingerLower = bands.Lower[^1];
            totalCount++;
            if (currentPrice > snapshot.BollingerMiddle) bullishCount++;
        }

        var vwap = indicators.VWAP(bars);
        snapshot.VWAP = vwap[^1];
        totalCount++;
        if (currentPrice > snapshot.VWAP) bullishCount++;

        snapshot.BullishIndicatorCount = bullishCount;
        snapshot.TotalIndicatorCount = totalCount;

        var atr = indicators.ATR(bars, AtrPeriod)[^1];
        var averageVolume = bars.TakeLast(VolumePeriod).Average(bar => (decimal)bar.Volume);
        var volumeRatio = averageVolume > 0 ? (decimal)bars[^1].Volume / averageVolume : 1m;
        return new StockIndicatorSnapshot(snapshot, atr, volumeRatio);

        void AddSma(decimal[] values, int period, Action<decimal> assign)
        {
            if (values.Length < period) return;
            var sma = indicators.SMA(values, period)[^1];
            assign(sma);
            totalCount++;
            if (currentPrice > sma) bullishCount++;
        }
    }

    public MarketTrendSnapshot CreateLongTrend(IReadOnlyList<OhlcvBar> bars)
    {
        if (bars.Count < LongTrendPeriod)
            throw new ArgumentException($"At least {LongTrendPeriod} bars are required.", nameof(bars));

        var closes = bars.Select(bar => bar.Close).ToArray();
        var movingAverage = indicators.SMA(closes, LongTrendPeriod)[^1];
        var price = closes[^1];
        return new MarketTrendSnapshot(price, movingAverage, price > movingAverage);
    }
}

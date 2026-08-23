using StockTrader.Engine.MarketData;

namespace StockTrader.Engine.Indicators;

public sealed partial class IndicatorCalculator
{
    public decimal[] VWAP(PriceBar[] bars)
    {
        var result = new decimal[bars.Length];
        decimal cumulativeTPV = 0;
        long cumulativeVolume = 0;

        for (int i = 0; i < bars.Length; i++)
        {
            var isIntraday = TimeFrameCatalog.IsIntraday(bars[i].TimeFrame);
            if (isIntraday && i > 0 && bars[i].Timestamp.Date != bars[i - 1].Timestamp.Date)
            {
                cumulativeTPV = 0;
                cumulativeVolume = 0;
            }
            var typicalPrice = (bars[i].High + bars[i].Low + bars[i].Close) / 3;
            cumulativeTPV += typicalPrice * bars[i].Volume;
            cumulativeVolume += bars[i].Volume;
            result[i] = cumulativeVolume > 0 ? cumulativeTPV / cumulativeVolume : 0;
        }
        return result;
    }

    public decimal[] ATR(PriceBar[] bars, int period = 14)
    {
        var result = new decimal[bars.Length];
        if (bars.Length <= 1) return result;

        var trueRanges = new decimal[bars.Length];
        trueRanges[0] = bars[0].High - bars[0].Low;

        for (int i = 1; i < bars.Length; i++)
        {
            var hl = bars[i].High - bars[i].Low;
            var hc = Math.Abs(bars[i].High - bars[i - 1].Close);
            var lc = Math.Abs(bars[i].Low - bars[i - 1].Close);
            trueRanges[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        decimal sum = 0;
        for (int i = 0; i < period && i < bars.Length; i++)
            sum += trueRanges[i];

        if (bars.Length >= period)
            result[period - 1] = sum / period;

        for (int i = period; i < bars.Length; i++)
        {
            result[i] = (result[i - 1] * (period - 1) + trueRanges[i]) / period;
        }
        return result;
    }

    public (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) MACD(
        decimal[] closes, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var fastEma = EMA(closes, fastPeriod);
        var slowEma = EMA(closes, slowPeriod);
        var macdLine = new decimal[closes.Length];

        for (int i = 0; i < closes.Length; i++)
            macdLine[i] = fastEma[i] - slowEma[i];

        var signalLine = EMA(macdLine, signalPeriod);
        var histogram = new decimal[closes.Length];

        for (int i = 0; i < closes.Length; i++)
            histogram[i] = macdLine[i] - signalLine[i];

        return (macdLine, signalLine, histogram);
    }

    public (decimal[] Upper, decimal[] Middle, decimal[] Lower) KeltnerChannel(
        PriceBar[] bars, int emaPeriod = 20, int atrPeriod = 10, decimal atrMultiplier = 1.5m)
    {
        // Extract closes inline to avoid a separate LINQ allocation.
        var closes = ExtractCloses(bars);
        var middle = EMA(closes, emaPeriod);
        var atr = ATR(bars, atrPeriod);
        var upper = new decimal[bars.Length];
        var lower = new decimal[bars.Length];

        for (int i = 0; i < bars.Length; i++)
        {
            upper[i] = middle[i] + atrMultiplier * atr[i];
            lower[i] = middle[i] - atrMultiplier * atr[i];
        }

        return (upper, middle, lower);
    }

    public decimal[] OBV(PriceBar[] bars)
    {
        var result = new decimal[bars.Length];
        if (bars.Length == 0) return result;
        result[0] = bars[0].Volume;
        for (int i = 1; i < bars.Length; i++)
        {
            if (bars[i].Close > bars[i - 1].Close)
                result[i] = result[i - 1] + bars[i].Volume;
            else if (bars[i].Close < bars[i - 1].Close)
                result[i] = result[i - 1] - bars[i].Volume;
            else
                result[i] = result[i - 1];
        }
        return result;
    }

    /// <summary>
    /// Extracts the Close prices from an PriceBar array into a decimal array.
    /// Avoids repeated LINQ .Select(b => b.Close).ToArray() allocations across callers.
    /// </summary>
    public static decimal[] ExtractCloses(PriceBar[] bars)
    {
        var closes = new decimal[bars.Length];
        for (int i = 0; i < bars.Length; i++)
            closes[i] = bars[i].Close;
        return closes;
    }
}

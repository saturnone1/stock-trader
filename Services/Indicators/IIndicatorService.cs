using StockTrader.Models;

namespace StockTrader.Services.Indicators;

public interface IIndicatorService
{
    decimal[] SMA(decimal[] closes, int period);
    decimal[] EMA(decimal[] closes, int period);
    decimal[] RSI(decimal[] closes, int period = 14);
    decimal[] CumulativeRsi(decimal[] closes, int rsiPeriod, int cumulativePeriod);
    (decimal[] Upper, decimal[] Middle, decimal[] Lower) BollingerBands(
        decimal[] closes, int period = 20, decimal stdDevMultiplier = 2.0m);
    decimal[] VWAP(OhlcvBar[] bars);
    decimal[] ATR(OhlcvBar[] bars, int period = 14);
    (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) MACD(
        decimal[] closes, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);
    (decimal[] Upper, decimal[] Middle, decimal[] Lower) KeltnerChannel(
        OhlcvBar[] bars, int emaPeriod = 20, int atrPeriod = 10, decimal atrMultiplier = 1.5m);
    decimal[] OBV(OhlcvBar[] bars);
}

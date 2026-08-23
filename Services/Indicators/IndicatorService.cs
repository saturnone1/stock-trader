using StockTrader.Engine.Indicators;
using StockTrader.Engine.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Indicators;

/// <summary>
/// 애플리케이션의 저장 모델을 저장소 독립 엔진 가격봉으로 변환하는 호환 어댑터입니다.
/// 모든 지표 수학은 <see cref="IndicatorCalculator"/> 한 구현에서 수행됩니다.
/// </summary>
public sealed class IndicatorService : IIndicatorService
{
    private static readonly IndicatorCalculator Calculator = new();

    public decimal[] SMA(decimal[] closes, int period) => Calculator.SMA(closes, period);
    public decimal[] EMA(decimal[] closes, int period) => Calculator.EMA(closes, period);
    public decimal[] RSI(decimal[] closes, int period = 14) => Calculator.RSI(closes, period);
    public decimal[] CumulativeRsi(decimal[] closes, int rsiPeriod, int cumulativePeriod) =>
        Calculator.CumulativeRsi(closes, rsiPeriod, cumulativePeriod);

    public (decimal[] Upper, decimal[] Middle, decimal[] Lower) BollingerBands(
        decimal[] closes, int period = 20, decimal stdDevMultiplier = 2m) =>
        Calculator.BollingerBands(closes, period, stdDevMultiplier);

    public decimal[] VWAP(OhlcvBar[] bars) => Calculator.VWAP(ToEngineBars(bars));
    public decimal[] ATR(OhlcvBar[] bars, int period = 14) =>
        Calculator.ATR(ToEngineBars(bars), period);

    public (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) MACD(
        decimal[] closes, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9) =>
        Calculator.MACD(closes, fastPeriod, slowPeriod, signalPeriod);

    public (decimal[] Upper, decimal[] Middle, decimal[] Lower) KeltnerChannel(
        OhlcvBar[] bars, int emaPeriod = 20, int atrPeriod = 10, decimal atrMultiplier = 1.5m) =>
        Calculator.KeltnerChannel(ToEngineBars(bars), emaPeriod, atrPeriod, atrMultiplier);

    public decimal[] OBV(OhlcvBar[] bars) => Calculator.OBV(ToEngineBars(bars));

    public static decimal[] ExtractCloses(OhlcvBar[] bars)
    {
        var closes = new decimal[bars.Length];
        for (var i = 0; i < bars.Length; i++) closes[i] = bars[i].Close;
        return closes;
    }

    public static PriceBar[] ToEngineBars(IReadOnlyList<OhlcvBar> bars)
    {
        var result = new PriceBar[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            result[i] = new(
                bar.Timestamp,
                bar.TimeFrame,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume,
                bar.Vwap);
        }
        return result;
    }
}

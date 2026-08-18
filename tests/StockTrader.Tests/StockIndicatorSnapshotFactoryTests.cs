using FluentAssertions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Analysis;
using StockTrader.Services.Indicators;

namespace StockTrader.Tests;

public class StockIndicatorSnapshotFactoryTests
{
    [Fact]
    public void Create_PreservesIndicatorAtrAndVolumeSnapshotComposition()
    {
        var indicators = new IndicatorService();
        var factory = new StockIndicatorSnapshotFactory(indicators);
        var bars = Bars(220);
        var closes = bars.Select(bar => bar.Close).ToArray();

        var result = factory.Create(bars);

        result.Indicators.RSI.Should().Be(indicators.RSI(closes, 14)[^1]);
        result.Indicators.SMA20.Should().Be(indicators.SMA(closes, 20)[^1]);
        result.Indicators.SMA50.Should().Be(indicators.SMA(closes, 50)[^1]);
        result.Indicators.SMA200.Should().Be(indicators.SMA(closes, 200)[^1]);
        var macd = indicators.MACD(closes, 12, 26, 9);
        result.Indicators.MACD.Should().Be(macd.MacdLine[^1]);
        result.Indicators.MACDSignal.Should().Be(macd.SignalLine[^1]);
        var bands = indicators.BollingerBands(closes, 20, 2m);
        result.Indicators.BollingerUpper.Should().Be(bands.Upper[^1]);
        result.Indicators.BollingerMiddle.Should().Be(bands.Middle[^1]);
        result.Indicators.BollingerLower.Should().Be(bands.Lower[^1]);
        result.Indicators.VWAP.Should().Be(indicators.VWAP(bars)[^1]);
        result.Indicators.TotalIndicatorCount.Should().Be(7);
        result.Atr.Should().Be(indicators.ATR(bars, 14)[^1]);
        result.VolumeRatio.Should().Be(
            (decimal)bars[^1].Volume / bars.TakeLast(20).Average(bar => (decimal)bar.Volume));
    }

    private static OhlcvBar[] Bars(int count) => Enumerable.Range(1, count)
        .Select(index => new OhlcvBar
        {
            Symbol = "SPY",
            Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index - 1),
            TimeFrame = TimeFrame.Daily,
            Open = index - 0.5m,
            High = index + 1m,
            Low = index - 1m,
            Close = index,
            Volume = 1_000 + index
        })
        .ToArray();
}

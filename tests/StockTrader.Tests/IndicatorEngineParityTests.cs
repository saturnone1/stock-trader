using FluentAssertions;
using StockTrader.Engine.Indicators;
using StockTrader.Services.Indicators;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class IndicatorEngineParityTests
{
    [Fact]
    public void StorageAdapterAndIndependentEngineProduceIdenticalIndicatorSeries()
    {
        var stored = Enumerable.Range(0, 48).Select(index => new OhlcvBar
        {
            Id = index + 10,
            Symbol = "TQQQ",
            Timestamp = new DateTime(2026, 1, 2, 9, 30, 0, DateTimeKind.Utc).AddMinutes(index),
            TimeFrame = TimeFrame.FiveMinute,
            Open = 100m + index / 3m,
            High = 101m + index / 3m,
            Low = 99m + index / 3m,
            Close = 100.25m + index / 3m + (index % 2 == 0 ? 0.2m : -0.1m),
            Volume = 1_000 + index * 17,
            Vwap = 100.1m + index / 3m
        }).ToArray();
        var engineBars = IndicatorService.ToEngineBars(stored);
        var closes = stored.Select(bar => bar.Close).ToArray();
        var adapter = new IndicatorService();
        var engine = new IndicatorCalculator();

        adapter.SMA(closes, 7).Should().Equal(engine.SMA(closes, 7));
        adapter.EMA(closes, 7).Should().Equal(engine.EMA(closes, 7));
        adapter.RSI(closes, 7).Should().Equal(engine.RSI(closes, 7));
        adapter.CumulativeRsi(closes, 2, 3).Should().Equal(engine.CumulativeRsi(closes, 2, 3));
        adapter.VWAP(stored).Should().Equal(engine.VWAP(engineBars));
        adapter.ATR(stored, 7).Should().Equal(engine.ATR(engineBars, 7));
        adapter.OBV(stored).Should().Equal(engine.OBV(engineBars));

        var adapterBands = adapter.BollingerBands(closes, 7, 2m);
        var engineBands = engine.BollingerBands(closes, 7, 2m);
        adapterBands.Upper.Should().Equal(engineBands.Upper);
        adapterBands.Middle.Should().Equal(engineBands.Middle);
        adapterBands.Lower.Should().Equal(engineBands.Lower);

        var adapterMacd = adapter.MACD(closes, 5, 10, 4);
        var engineMacd = engine.MACD(closes, 5, 10, 4);
        adapterMacd.MacdLine.Should().Equal(engineMacd.MacdLine);
        adapterMacd.SignalLine.Should().Equal(engineMacd.SignalLine);
        adapterMacd.Histogram.Should().Equal(engineMacd.Histogram);

        var adapterKeltner = adapter.KeltnerChannel(stored, 7, 5, 1.5m);
        var engineKeltner = engine.KeltnerChannel(engineBars, 7, 5, 1.5m);
        adapterKeltner.Upper.Should().Equal(engineKeltner.Upper);
        adapterKeltner.Middle.Should().Equal(engineKeltner.Middle);
        adapterKeltner.Lower.Should().Equal(engineKeltner.Lower);
    }

    [Fact]
    public void EnginePriceBarsContainNoStorageIdentity()
    {
        typeof(StockTrader.Engine.MarketData.PriceBar).GetProperty("Id").Should().BeNull();
        typeof(StockTrader.Engine.MarketData.PriceBar).GetProperty("Symbol").Should().BeNull();
    }
}

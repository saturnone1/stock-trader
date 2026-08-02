using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class CumulativeRsi2DetectorTests
{
    private static CumulativeRsi2Detector CreateSut(
        decimal currentCumulativeRsi,
        decimal longTrendMa,
        decimal exitSma,
        decimal atr = 2m,
        CumulativeRsi2Config? config = null)
    {
        var indicatorsMock = new Mock<IIndicatorService>();

        indicatorsMock.Setup(i => i.CumulativeRsi(It.IsAny<decimal[]>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns<decimal[], int, int>((closes, _, _) =>
            {
                var result = new decimal[closes.Length];
                result[^1] = currentCumulativeRsi;
                return result;
            });

        indicatorsMock.Setup(i => i.SMA(It.IsAny<decimal[]>(), It.IsAny<int>()))
            .Returns<decimal[], int>((closes, period) =>
            {
                var value = period == 200 ? longTrendMa : exitSma;
                return Enumerable.Repeat(value, closes.Length).ToArray();
            });

        indicatorsMock.Setup(i => i.ATR(It.IsAny<OhlcvBar[]>(), It.IsAny<int>()))
            .Returns<OhlcvBar[], int>((bars, _) => Enumerable.Repeat(atr, bars.Length).ToArray());

        var settings = new PatternSettings
        {
            CumulativeRsi2 = config ?? new CumulativeRsi2Config()
        };

        var snapshotMock = new Mock<IOptionsSnapshot<PatternSettings>>();
        snapshotMock.Setup(x => x.Value).Returns(settings);

        return new CumulativeRsi2Detector(indicatorsMock.Object, snapshotMock.Object);
    }

    private static OhlcvBar[] CreateBars(int count = 210, decimal lastClose = 110m)
    {
        var bars = Enumerable.Range(0, count)
            .Select(i => new OhlcvBar
            {
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1_000_000,
                Timestamp = DateTime.UtcNow.AddDays(-count + i)
            })
            .ToArray();

        bars[^1].Close = lastClose;
        bars[^1].High = lastClose + 1m;
        bars[^1].Low = lastClose - 1m;

        return bars;
    }

    [Fact]
    public async Task DetectAsync_returns_signal_when_cumulative_rsi_is_oversold_and_above_trend_ma()
    {
        var sut = CreateSut(currentCumulativeRsi: 8m, longTrendMa: 100m, exitSma: 108m);

        var result = await sut.DetectAsync("AAPL", CreateBars(), new MarketRegime());

        result.Should().NotBeNull();
        result!.PatternType.Should().Be(PatternType.CumulativeRsi2);
        result.StopLossPrice.Should().Be(107m);
        result.TargetPrice.Should().Be(126m);
        result.Confidence.Should().BeGreaterThan(0.55m);
    }

    [Fact]
    public async Task DetectAsync_returns_null_when_cumulative_rsi_is_above_entry_threshold()
    {
        var sut = CreateSut(currentCumulativeRsi: 12m, longTrendMa: 100m, exitSma: 108m);

        var result = await sut.DetectAsync("AAPL", CreateBars(), new MarketRegime());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_returns_null_when_price_is_below_long_trend_ma()
    {
        var sut = CreateSut(currentCumulativeRsi: 8m, longTrendMa: 115m, exitSma: 108m);

        var result = await sut.DetectAsync("AAPL", CreateBars(lastClose: 110m), new MarketRegime());

        result.Should().BeNull();
    }
}

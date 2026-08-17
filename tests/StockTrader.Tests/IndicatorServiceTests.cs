using FluentAssertions;
using StockTrader.Services.Indicators;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class IndicatorServiceTests
{
    [Fact]
    public void CumulativeRsi_sums_recent_rsi_values_after_warmup()
    {
        var sut = new IndicatorService();
        var closes = new[] { 10m, 9m, 8m, 9m, 10m, 11m, 10.5m };

        var rsi = sut.RSI(closes, 2);
        var cumulative = sut.CumulativeRsi(closes, 2, 2);

        cumulative[0].Should().Be(0m);
        cumulative[1].Should().Be(0m);
        cumulative[2].Should().Be(0m);

        for (int i = 3; i < closes.Length; i++)
            cumulative[i].Should().Be(rsi[i] + rsi[i - 1]);
    }

    [Fact]
    public void Vwap_ResetsAtEachIntradaySession()
    {
        var sut = new IndicatorService();
        var bars = new[]
        {
            Bar(new DateTime(2024, 1, 2, 15, 55, 0), 100m),
            Bar(new DateTime(2024, 1, 2, 16, 0, 0), 102m),
            Bar(new DateTime(2024, 1, 3, 9, 30, 0), 200m)
        };

        var result = sut.VWAP(bars);

        result[1].Should().Be(101m);
        result[2].Should().Be(200m);
    }

    private static OhlcvBar Bar(DateTime timestamp, decimal price) => new()
    {
        Timestamp = timestamp,
        TimeFrame = TimeFrame.FiveMinute,
        Open = price,
        High = price,
        Low = price,
        Close = price,
        Volume = 100
    };
}

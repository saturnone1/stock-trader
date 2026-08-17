using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Models;

namespace StockTrader.Tests;

public class BacktestTimelineTests
{
    [Fact]
    public void BuildSimulationTimeline_PreservesEveryIntradayBar()
    {
        var start = new DateTime(2026, 8, 17, 13, 30, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 4).Select(index => new OhlcvBar
        {
            Timestamp = start.AddMinutes(index),
            Open = 100,
            High = 101,
            Low = 99,
            Close = 100,
            Volume = 1_000
        }).ToArray();
        var zeros = new decimal[bars.Length];
        var prepared = new PreparedSymbolData(
            bars, zeros, zeros, zeros, zeros, zeros,
            bars.Select((bar, index) => (bar.Timestamp, index)).ToDictionary(pair => pair.Timestamp, pair => pair.index));

        var timeline = BacktestTimeline.Build([prepared], start);

        timeline.Should().Equal(bars.Select(bar => bar.Timestamp));
    }
}

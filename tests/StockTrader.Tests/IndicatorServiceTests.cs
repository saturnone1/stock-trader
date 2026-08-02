using FluentAssertions;
using StockTrader.Services.Indicators;

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
}

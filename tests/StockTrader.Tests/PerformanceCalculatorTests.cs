using FluentAssertions;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class PerformanceCalculatorTests
{
    [Fact]
    public void ComputeKellyFraction_ReturnsZero_WhenAverageWinIsZero()
    {
        var kelly = PerformanceCalculator.ComputeKellyFraction(
            winRate: 0m,
            avgWinPct: 0m,
            avgLossPct: 5m);

        kelly.Should().Be(0m);
    }

    [Fact]
    public void ComputeKellyFraction_ReturnsZero_WhenAverageLossIsZero()
    {
        var kelly = PerformanceCalculator.ComputeKellyFraction(
            winRate: 0.5m,
            avgWinPct: 5m,
            avgLossPct: 0m);

        kelly.Should().Be(0m);
    }
}

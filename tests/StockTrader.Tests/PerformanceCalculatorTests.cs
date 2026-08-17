using FluentAssertions;
using StockTrader.Services.Backtest;
using StockTrader.Models;

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

    [Fact]
    public void ComputePerStrategyStats_SeparatesCustomStrategies()
    {
        var trades = new List<TradeRecord>
        {
            new() { PatternType = PatternType.Custom, CustomPatternName = "반등", PnL = 100m, PnLPercent = 0.10m },
            new() { PatternType = PatternType.Custom, CustomPatternName = "돌파", PnL = -50m, PnLPercent = -0.05m }
        };

        var result = PerformanceCalculator.ComputePerStrategyStats(trades);

        result.Keys.Should().BeEquivalentTo("반등", "돌파");
        result["반등"].WinRate.Should().Be(1m);
        result["돌파"].WinRate.Should().Be(0m);
    }
}

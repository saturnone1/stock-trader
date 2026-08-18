using FluentAssertions;
using StockTrader.Services.Backtest;
using StockTrader.Models;
using StockTrader.Models.Enums;

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

        var calculatedAt = new DateTime(2026, 8, 19, 5, 0, 0, DateTimeKind.Utc);
        var result = PerformanceCalculator.ComputePerStrategyStats(trades, calculatedAt);

        result.Keys.Should().BeEquivalentTo("반등", "돌파");
        result["반등"].WinRate.Should().Be(1m);
        result["돌파"].WinRate.Should().Be(0m);
        result.Values.Should().OnlyContain(stats => stats.LastUpdated == calculatedAt);
    }

    [Fact]
    public void AggregateTradeCycles_TreatsPartialExitsAsOneTrade()
    {
        var entryTime = new DateTime(2024, 1, 2);
        var executions = new List<TradeRecord>
        {
            new() { Symbol = "TQQQ", PatternType = PatternType.Custom, CustomPatternName = "분할", EntryTime = entryTime, ExitTime = entryTime.AddDays(1), EntryPrice = 100m, ExitPrice = 110m, Quantity = 5, PnL = 50m, PnLPercent = 0.10m },
            new() { Symbol = "TQQQ", PatternType = PatternType.Custom, CustomPatternName = "분할", EntryTime = entryTime, ExitTime = entryTime.AddDays(2), EntryPrice = 100m, ExitPrice = 95m, Quantity = 5, PnL = -25m, PnLPercent = -0.05m }
        };

        var cycles = PerformanceCalculator.AggregateTradeCycles(executions);

        cycles.Should().ContainSingle();
        cycles[0].PnL.Should().Be(25m);
        cycles[0].PnLPercent.Should().Be(0.025m);
        cycles[0].IsWin.Should().BeTrue();
    }

}

using FluentAssertions;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class PatternOverrideMergerTests
{
    [Fact]
    public void Merge_AppliesCurrentTqqqExecutionSettingsWithoutMutatingBase()
    {
        var baseSettings = new PatternSettings();
        var overrides = new PatternParameterOverrides
        {
            Tqqq_SmaPeriod = 250,
            Tqqq_FixedStopPercent = 0.04m,
            Tqqq_SmaStopMultiplier = 0.985m,
            Tqqq_TargetSmaMultiplier = 1.4m,
            Tqqq_MinimumTargetReturnPercent = 0.08m,
        };

        var merged = PatternOverrideMerger.Merge(baseSettings, overrides);

        merged.Tqqq200Sma.SmaPeriod.Should().Be(250);
        merged.Tqqq200Sma.FixedStopPercent.Should().Be(0.04m);
        merged.Tqqq200Sma.SmaStopMultiplier.Should().Be(0.985m);
        merged.Tqqq200Sma.TargetSmaMultiplier.Should().Be(1.4m);
        merged.Tqqq200Sma.MinimumTargetReturnPercent.Should().Be(0.08m);
        baseSettings.Tqqq200Sma.SmaPeriod.Should().Be(200);
        baseSettings.Tqqq200Sma.SmaStopMultiplier.Should().Be(0.99m);
    }
}

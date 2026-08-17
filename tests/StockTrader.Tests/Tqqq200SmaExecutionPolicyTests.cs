using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class Tqqq200SmaExecutionPolicyTests
{
    [Fact]
    public void ResolveEntryLevels_PreservesEstablishedDefaultRules()
    {
        var levels = Tqqq200SmaExecutionPolicy.ResolveEntryLevels(
            entryPrice: 102m,
            trendSma: 100m,
            fixedStopPercent: 0.059m,
            smaStopMultiplier: 0.99m,
            targetSmaMultiplier: 1.50m,
            minimumTargetReturnPercent: 0.10m);

        levels.Should().Be(new TqqqEntryLevels(99m, 150m));
    }

    [Fact]
    public void ResolveEntryLevels_UsesMinimumReturnWhenTrendTargetIsBelowEntry()
    {
        var levels = Tqqq200SmaExecutionPolicy.ResolveEntryLevels(
            entryPrice: 160m,
            trendSma: 100m,
            fixedStopPercent: 0.059m,
            smaStopMultiplier: 0.99m,
            targetSmaMultiplier: 1.50m,
            minimumTargetReturnPercent: 0.10m);

        levels!.TargetPrice.Should().Be(176m);
    }

    [Theory]
    [InlineData(0, 100, 0.059, 0.99, 1.5, 0.1)]
    [InlineData(100, 0, 0.059, 0.99, 1.5, 0.1)]
    [InlineData(100, 100, 1, 0.99, 1.5, 0.1)]
    [InlineData(100, 100, 0.059, 0, 1.5, 0.1)]
    public void ResolveEntryLevels_RejectsUnsafeInputs(
        decimal entryPrice,
        decimal trendSma,
        decimal fixedStopPercent,
        decimal smaStopMultiplier,
        decimal targetSmaMultiplier,
        decimal minimumTargetReturnPercent)
    {
        Tqqq200SmaExecutionPolicy.ResolveEntryLevels(
                entryPrice, trendSma, fixedStopPercent, smaStopMultiplier,
                targetSmaMultiplier, minimumTargetReturnPercent)
            .Should().BeNull();
    }

    [Fact]
    public void ResolveEntryLevels_RejectsAStopAtOrAboveEntry()
    {
        Tqqq200SmaExecutionPolicy.ResolveEntryLevels(
                entryPrice: 100m,
                trendSma: 102m,
                fixedStopPercent: 0.059m,
                smaStopMultiplier: 0.99m,
                targetSmaMultiplier: 1.50m,
                minimumTargetReturnPercent: 0.10m)
            .Should().BeNull();
    }

    [Fact]
    public void RequiredCalendarLookbackDays_CoversConfiguredTradingPeriod()
    {
        Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(200).Should().Be(310);
        Tqqq200SmaExecutionPolicy.RequiredCalendarLookbackDays(0).Should().Be(0);
    }
}

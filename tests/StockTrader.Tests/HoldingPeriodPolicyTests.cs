using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class HoldingPeriodPolicyTests
{
    [Fact]
    public void HasReachedDailyBarLimit_CountsObservedSessionsInsteadOfCalendarDays()
    {
        var opened = new DateTime(2026, 1, 16, 14, 30, 0, DateTimeKind.Utc);
        var bars = new[]
        {
            Bar(2026, 1, 16),
            Bar(2026, 1, 20), // weekend and US holiday do not create synthetic bars
            Bar(2026, 1, 21),
        };

        HoldingPeriodPolicy.HasReachedDailyBarLimit(opened, bars, 2).Should().BeTrue();
        HoldingPeriodPolicy.HasReachedDailyBarLimit(opened, bars, 3).Should().BeFalse();
    }

    [Fact]
    public void HasReachedDailyBarLimit_ZeroDisablesExit()
    {
        HoldingPeriodPolicy.HasReachedDailyBarLimit(
            new DateTime(2020, 1, 1), [Bar(2026, 1, 1)], 0).Should().BeFalse();
    }

    private static OhlcvBar Bar(int year, int month, int day) => new()
    {
        Timestamp = new DateTime(year, month, day, 21, 0, 0, DateTimeKind.Utc),
    };
}

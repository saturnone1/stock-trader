using FluentAssertions;
using StockTrader.Engine.Portfolio;

namespace StockTrader.Tests;

public class PortfolioAccountingLedgerTests
{
    [Fact]
    public void RecordMarkedEquity_CombinesRealizedAndUnrealizedPnlAndTracksDrawdown()
    {
        var startedAt = new DateTime(2025, 1, 1);
        var ledger = new PortfolioAccountingLedger(1_000m, startedAt);
        ledger.ApplyRealizedPnl(50m);
        ledger.ObservePrice("AAA", 110m);
        ledger.RecordMarkedEquity(
            startedAt.AddDays(1), [new PositionMark("AAA", 100m, 10)]);

        ledger.ObservePrice("AAA", 90m);
        ledger.RecordMarkedEquity(
            startedAt.AddDays(2), [new PositionMark("AAA", 100m, 10)]);

        ledger.CurrentEquity.Should().Be(1_050m);
        ledger.EquityCurve.Select(point => point.Equity).Should().Equal(1_000m, 1_150m, 950m);
        ledger.MaxDrawdown.Should().BeApproximately(200m / 1_150m, 0.0000001m);
    }

    [Fact]
    public void DailyLossLimit_ResetsOnlyWhenTradingDayChanges()
    {
        var ledger = new PortfolioAccountingLedger(1_000m, new DateTime(2025, 1, 1));
        var day = new DateOnly(2025, 1, 2);
        ledger.BeginTradingDay(day);
        ledger.ApplyRealizedPnl(-100m);

        ledger.HasReachedDailyLossLimit(0.10m).Should().BeTrue();
        ledger.BeginTradingDay(day);
        ledger.HasReachedDailyLossLimit(0.10m).Should().BeTrue();
        ledger.BeginTradingDay(day.AddDays(1));
        ledger.HasReachedDailyLossLimit(0.10m).Should().BeFalse();
    }

    [Fact]
    public void RecordMarkedEquity_ReplacesTheLastPointAtTheSameTimestamp()
    {
        var timestamp = new DateTime(2025, 1, 1);
        var ledger = new PortfolioAccountingLedger(1_000m, timestamp);

        ledger.RecordMarkedEquity(timestamp, []);

        ledger.EquityCurve.Should().ContainSingle();
    }
}

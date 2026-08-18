using FluentAssertions;
using StockTrader.Application.Research;

namespace StockTrader.Tests;

public sealed class SecFinancialSyncPolicyTests
{
    [Fact]
    public void ResolveSymbolsUsesRequestedThenConfiguredThenActivePrecedence()
    {
        SecFinancialSyncPolicy.ResolveSymbols(
                [" brk.b ", "BRK-B", "aapl"],
                "MSFT,NVDA",
                ["TQQQ"],
                1)
            .Should().Equal("BRK-B", "AAPL");

        SecFinancialSyncPolicy.ResolveSymbols(
                null,
                " msft,MSFT,nvda ",
                ["TQQQ"],
                1)
            .Should().Equal("MSFT");

        SecFinancialSyncPolicy.ResolveSymbols(
                null,
                null,
                [" tqqq ", "TQQQ", "qqq"],
                2)
            .Should().Equal("TQQQ", "QQQ");
    }

    [Fact]
    public void IntervalUsesCompletedBoundaryAndClampsMinimumHours()
    {
        var now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        SecFinancialSyncPolicy.IsWithinAutomaticInterval(now.AddHours(-1), now, 1)
            .Should().BeTrue();
        SecFinancialSyncPolicy.IsWithinAutomaticInterval(
                now.AddHours(-1).AddTicks(-1), now, 0)
            .Should().BeFalse();
        SecFinancialSyncPolicy.IsWithinAutomaticInterval(null, now, 12)
            .Should().BeFalse();
    }

    [Fact]
    public void RunIdentityRetainsCompatibleLabelsAndFingerprint()
    {
        var symbols = Enumerable.Range(1, 11).Select(index => $"S{index}").ToArray();
        var startedAt = new DateTime(2026, 8, 18, 3, 4, 5, DateTimeKind.Utc);

        SecFinancialSyncPolicy.BuildRunLabel(symbols, explicitlyRequested: true)
            .Should().Be("SEC:S1,S2,S3,S4,S5,S6,S7,S8,S9,S10 (+1)");
        SecFinancialSyncPolicy.BuildRunLabel(symbols, explicitlyRequested: false)
            .Should().Be("SEC:auto:11");
        SecFinancialSyncPolicy.BuildFingerprint(symbols, startedAt)
            .Should().Be("SEC|20260818030405|S1,S2,S3,S4,S5,S6,S7,S8,S9,S10,S11");
    }
}

using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.Application.Reporting;
using TimeZoneConverter;

namespace StockTrader.Tests;

public sealed class DailyReportPolicyTests
{
    private static readonly TimeZoneInfo EasternTime =
        TZConvert.GetTimeZoneInfo("America/New_York");
    private static readonly TimeZoneInfo KoreanTime =
        TZConvert.GetTimeZoneInfo("Asia/Seoul");

    [Fact]
    public void ResolveMarketDay_UsesBothLocalMidnightsAcrossDstBoundary()
    {
        var observation = DateTimeOffset.Parse("2026-03-08T12:00:00Z");

        var window = DailyReportPolicy.ResolveMarketDay(observation, EasternTime);

        window.ReportDate.Should().Be(new DateOnly(2026, 3, 8));
        window.FromUtc.Should().Be(DateTime.Parse("2026-03-08T05:00:00Z").ToUniversalTime());
        window.ToUtc.Should().Be(DateTime.Parse("2026-03-09T04:00:00Z").ToUniversalTime());
        (window.ToUtc - window.FromUtc).Should().Be(TimeSpan.FromHours(23));
    }

    /// <summary>실제 거래소 캘린더를 연결해 휴장일 동작까지 함께 고정한다.</summary>
    private static bool IsTradingDay(DateOnly date) =>
        ExchangeCalendarCatalog.GetTradingDay(MarketRegion.UnitedStates, date).IsTradingDay;

    [Fact]
    public void CalculateDelay_KoreanOverrideSkipsCandidatesWhoseUsDateIsWeekend()
    {
        var observation = DateTimeOffset.Parse("2026-08-21T23:00:00Z");

        var delay = DailyReportPolicy.CalculateDelay(
            observation,
            new TimeOnly(7, 30),
            KoreanTime,
            EasternTime,
            IsTradingDay);

        delay.Should().Be(TimeSpan.FromHours(71.5));
    }

    [Fact]
    public void Create_UsesAccountEquityAndRetainsFullSignalCountWithStableTopFive()
    {
        var signals = Enumerable.Range(1, 60)
            .Select(index => new DailyReportSignalSnapshot(
                $"S{index:00}",
                PatternType.Breakout,
                100m + index,
                new DateTime(2026, 8, 18, 12, index % 60, 0, DateTimeKind.Utc)))
            .ToArray();
        var activity = new DailyReportActivitySnapshot(
            [
                new("aapl", 100m, 10, 150m, Utc(15)),
                new("AAPL", 110m, 5, -50m, Utc(16)),
                new("MSFT", 200m, 5, 0m, Utc(17))
            ],
            signals);

        var report = DailyReportPolicy.Create(
            new DateOnly(2026, 8, 18),
            activity,
            accountEquity: 10_000m);

        report.TotalSignals.Should().Be(60);
        report.ExecutedTrades.Should().Be(3);
        report.DailyPnl.Should().Be(100m);
        report.DailyPnlPercent.Should().Be(1m);
        report.TopSignals.Should().HaveCount(5);
        report.TopSignals[0].Should().StartWith("S59 ");
        report.ExecutedSymbols.Should().Equal("aapl", "MSFT");
    }

    [Fact]
    public void Create_MissingEquityFallsBackToEntryValue()
    {
        var activity = new DailyReportActivitySnapshot(
            [new("AAPL", 100m, 10, 50m, Utc(16))],
            []);

        var report = DailyReportPolicy.Create(
            new DateOnly(2026, 8, 18),
            activity,
            accountEquity: null);

        report.DailyPnlPercent.Should().Be(5m);
    }

    private static DateTime Utc(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);
}

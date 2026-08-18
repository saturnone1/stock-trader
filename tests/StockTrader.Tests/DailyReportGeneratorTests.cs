using FluentAssertions;
using Moq;
using StockTrader.Application.Reporting;
using StockTrader.Application.Settings;
using TimeZoneConverter;

namespace StockTrader.Tests;

public sealed class DailyReportGeneratorTests
{
    [Theory]
    [InlineData("07:30", 7, 30)]
    [InlineData("invalid", null, null)]
    [InlineData(null, null, null)]
    public async Task ScheduleQuery_ParsesOnlyValidatedKoreanClockValues(
        string? value,
        int? expectedHour,
        int? expectedMinute)
    {
        var settings = new Mock<ISettingsManagementStore>();
        settings.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManagedSettings { DailyReportTimeKst = value });

        var result = await new DailyReportScheduleQuery(settings.Object)
            .GetKoreanReportTimeAsync();

        if (expectedHour.HasValue)
            result.Should().Be(new TimeOnly(expectedHour.Value, expectedMinute!.Value));
        else
            result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAndPublishAsync_UsesOneObservedMarketDayAndPublishesProjection()
    {
        var activityStore = new Mock<IDailyReportActivityStore>();
        var equityReader = new Mock<IActiveAccountEquityReader>();
        var publisher = new Mock<IDailyReportPublisher>();
        var activity = new DailyReportActivitySnapshot(
            [new("AAPL", 100m, 10, 75m, Utc(18))],
            [new("AAPL", PatternType.Breakout, 101m, Utc(14))]);
        activityStore
            .Setup(store => store.ReadAsync(
                new DateTime(2026, 8, 18, 4, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 19, 4, 0, 0, DateTimeKind.Utc),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(activity);
        equityReader.Setup(reader => reader.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(15_000m);
        var sut = new DailyReportGenerator(
            activityStore.Object,
            equityReader.Object,
            publisher.Object,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-18T20:00:00Z")));

        var report = await sut.GenerateAndPublishAsync(
            TZConvert.GetTimeZoneInfo("America/New_York"));

        report.ReportDate.Should().Be(new DateOnly(2026, 8, 18));
        report.DailyPnlPercent.Should().Be(0.5m);
        publisher.Verify(item => item.PublishAsync(
            report,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DateTime Utc(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset observation) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => observation;
    }
}

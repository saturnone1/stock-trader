using System.Text.Json;
using FluentAssertions;
using Moq;
using StockTrader.Application.StrategyPreview;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;
using StockTrader.Services.StrategyPreview;

namespace StockTrader.Tests;

public class PatternPreviewServiceTests
{
    [Fact]
    public async Task PreviewAsync_PreparesDataCompilesOnceAndRunsThePureEngine()
    {
        var bars = Bars();
        var repository = new Mock<IOhlcvRepository>();
        repository.Setup(repo => repo.GetBarsAsync(
                "AAA", TimeFrame.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars.ToList());
        repository.Setup(repo => repo.GetBarsAsync(
                "SPY", TimeFrame.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OhlcvBar>());
        var feed = new Mock<IDataFeedService>();
        feed.Setup(service => service.GetHistoricalBarsAsync(
                It.IsAny<string>(), It.IsAny<TimeFrame>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OhlcvBar>());
        var feeds = new Mock<IDataFeedServiceFactory>();
        feeds.Setup(factory => factory.GetServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed.Object);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
        var indicators = new IndicatorService();
        var service = new PatternPreviewService(
            repository.Object,
            feeds.Object,
            indicators,
            new CustomStrategyDetectorFactory(indicators, clock),
            new PatternPreviewSimulationEngine(),
            clock);

        var outcome = await service.PreviewAsync(new PatternPreviewQuery(
            " aaa ",
            Pattern(),
            TimeFrame.Daily,
            bars[50].Timestamp,
            bars[50].Timestamp));

        outcome.Kind.Should().Be(PatternPreviewOutcomeKind.Success);
        outcome.Result!.Symbol.Should().Be("AAA");
        outcome.Result.Bars.Should().ContainSingle();
        outcome.Result.Markers.Should().ContainSingle(marker => marker.Type == "ENTRY");
        outcome.Result.Summary.OpenPosition.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewAsync_ProviderFailureHasAnExplicitOutcome()
    {
        var repository = new Mock<IOhlcvRepository>();
        repository.Setup(repo => repo.GetBarsAsync(
                It.IsAny<string>(), It.IsAny<TimeFrame>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OhlcvBar>());
        var feed = new Mock<IDataFeedService>();
        feed.Setup(service => service.GetHistoricalBarsAsync(
                It.IsAny<string>(), It.IsAny<TimeFrame>(), It.IsAny<DateTime>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider unavailable"));
        var feeds = new Mock<IDataFeedServiceFactory>();
        feeds.Setup(factory => factory.GetServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed.Object);
        var indicators = new IndicatorService();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero));
        var service = new PatternPreviewService(
            repository.Object,
            feeds.Object,
            indicators,
            new CustomStrategyDetectorFactory(indicators, clock),
            new PatternPreviewSimulationEngine(),
            clock);

        var outcome = await service.PreviewAsync(new PatternPreviewQuery(
            "AAA", Pattern(), TimeFrame.Daily,
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 10)));

        outcome.Kind.Should().Be(PatternPreviewOutcomeKind.ProviderUnavailable);
        outcome.Error.Should().Contain("현재 데이터 제공자");
    }

    private static CustomPatternDefinition Pattern() => new()
    {
        Name = "preview-use-case",
        EntryRulesJson = JsonSerializer.Serialize(new[]
        {
            new EntryRule
            {
                Indicator = "PRICE_CHANGE",
                Operator = ">=",
                Value = 0m,
                Params = new Dictionary<string, decimal> { ["bars"] = 1m }
            }
        })
    };

    private static OhlcvBar[] Bars() => Enumerable.Range(0, 52)
        .Select(index => new OhlcvBar
        {
            Symbol = "AAA",
            TimeFrame = TimeFrame.Daily,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = 1_000_000
        })
        .ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Statistics;

namespace StockTrader.Tests;

public sealed class StatisticsServiceClockTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DirectCalculationUsesInjectedObservationTime()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new StatisticsService(
            new Mock<IPatternStatsRepository>().Object,
            new Mock<ITradeHistoryStore>().Object,
            cache,
            new FixedTimeProvider(ObservedAt),
            Options.Create(new PatternStatisticsSettings { CacheMinutes = 5 }),
            NullLogger<StatisticsService>.Instance);

        var stats = await service.ComputeStatsAsync(
            PatternType.Breakout,
            [Trade(PatternType.Breakout, 0.1m)]);

        stats.LastUpdated.Should().Be(ObservedAt.UtcDateTime);
        stats.SampleSize.Should().Be(1);
        stats.WinRate.Should().Be(1m);
    }

    [Fact]
    public async Task RefreshSamplesOneClockInstantForTheWholeBatch()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new Mock<IPatternStatsRepository>();
        List<PatternStats>? saved = null;
        repository.Setup(value => value.SaveBatchAsync(
                It.IsAny<IEnumerable<PatternStats>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<PatternStats>, CancellationToken>((items, _) =>
                saved = items.ToList())
            .Returns(Task.CompletedTask);
        repository.Setup(value => value.DeleteStaleAsync(
                It.IsAny<ISet<(PatternType PatternType, string? Symbol)>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var trades = new Mock<ITradeHistoryStore>();
        trades.Setup(value => value.GetTradesAsync(
                null,
                null,
                null,
                0,
                int.MaxValue,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Trade(PatternType.Breakout, 0.1m)]);
        var service = new StatisticsService(
            repository.Object,
            trades.Object,
            cache,
            new FixedTimeProvider(ObservedAt),
            Options.Create(new PatternStatisticsSettings { CacheMinutes = 5 }),
            NullLogger<StatisticsService>.Instance);

        await service.RefreshAllStatsAsync();

        saved.Should().NotBeNullOrEmpty();
        saved!.Should().OnlyContain(stats => stats.LastUpdated == ObservedAt.UtcDateTime);
        saved.Should().ContainSingle(stats =>
            stats.PatternType == PatternType.Breakout && stats.SampleSize == 1);
    }

    private static TradeRecord Trade(PatternType pattern, decimal returnRate) => new()
    {
        PatternType = pattern,
        PnL = returnRate > 0 ? 100 : -100,
        PnLPercent = returnRate,
        EntryTime = ObservedAt.UtcDateTime.AddDays(-1),
        ExitTime = ObservedAt.UtcDateTime
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

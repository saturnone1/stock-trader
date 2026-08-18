using FluentAssertions;
using Moq;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Statistics;

namespace StockTrader.Tests;

public class PatternStatisticsQueryTests
{
    [Fact]
    public async Task GetByExpectancyAsync_MapsStorageEntitiesAndRanksDeterministically()
    {
        var service = new Mock<IStatisticsService>();
        service.Setup(item => item.GetAllStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PatternStats>
            {
                Stats(PatternType.Breakout, "MSFT", 0.50m, 0.10m, 0.10m),
                Stats(PatternType.GapUpPullback, null, 0.75m, 0.10m, 0.05m),
                Stats(PatternType.Breakout, "AAPL", 0.50m, 0.10m, 0.10m)
            });
        var sut = new PatternStatisticsQuery(service.Object);

        var result = await sut.GetByExpectancyAsync();

        result.Select(stat => stat.PatternType).Should().Equal(
            PatternType.GapUpPullback,
            PatternType.Breakout,
            PatternType.Breakout);
        result.Skip(1).Select(stat => stat.Symbol).Should().Equal("AAPL", "MSFT");
        result[0].Expectancy.Should().Be(0.0625m);
        result[0].ProfitFactor.Should().Be(6m);
    }

    [Fact]
    public async Task GetAllAsync_PerfectWinRateDoesNotDivideByZero()
    {
        var service = new Mock<IStatisticsService>();
        service.Setup(item => item.GetAllStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PatternStats>
            {
                Stats(PatternType.Breakout, null, 1m, 0.10m, 0.05m)
            });
        var sut = new PatternStatisticsQuery(service.Object);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
        result[0].ProfitFactor.Should().Be(0m);
    }

    private static PatternStats Stats(
        PatternType pattern,
        string? symbol,
        decimal winRate,
        decimal avgWin,
        decimal avgLoss) => new()
    {
        PatternType = pattern,
        Symbol = symbol,
        SampleSize = 10,
        WinRate = winRate,
        AvgWinPercent = avgWin,
        AvgLossPercent = avgLoss,
        MaxDrawdownPercent = 0.1m,
        LastUpdated = new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc)
    };
}

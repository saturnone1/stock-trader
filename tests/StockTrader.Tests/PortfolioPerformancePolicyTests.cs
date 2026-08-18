using FluentAssertions;
using Moq;
using StockTrader.Api.Contracts;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Statistics;
using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Portfolio;

namespace StockTrader.Tests;

public class PortfolioPerformancePolicyTests
{
    [Fact]
    public void Evaluate_UsesInitialAccountEquityAndRecognizesAnOpeningLoss()
    {
        var firstExit = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc);
        var trades = new[]
        {
            Trade(3, firstExit.AddDays(2), -220m, -0.20m),
            Trade(1, firstExit, -100m, -0.10m),
            Trade(2, firstExit.AddDays(1), 200m, 0.20m)
        };

        var result = PortfolioPerformancePolicy.Evaluate(trades, 1_000m, []);

        result.TotalTrades.Should().Be(3);
        result.WinRate.Should().Be(1m / 3m);
        result.AvgWinPercent.Should().Be(0.20m);
        result.AvgLossPercent.Should().Be(-0.15m);
        result.MaxDrawdown.Should().Be(0.20m);
        result.EquityCurve.Select(point => point.CumulativePnL)
            .Should().Equal(-100m, 100m, -120m);
        result.EquityCurve.Select(point => point.ExitTime)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Evaluate_UsesTradeIdToBreakEqualExitTimeTies()
    {
        var exitTime = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc);
        var trades = new[]
        {
            Trade(2, exitTime, 200m, 0.20m),
            Trade(1, exitTime, -100m, -0.10m)
        };

        var result = PortfolioPerformancePolicy.Evaluate(trades, 1_000m, []);

        result.EquityCurve.Select(point => point.CumulativePnL)
            .Should().Equal(-100m, 100m);
        result.MaxDrawdown.Should().Be(0.10m);
    }

    [Fact]
    public void Evaluate_RejectsNonPositiveInitialEquity()
    {
        var act = () => PortfolioPerformancePolicy.Evaluate([], 0m, []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResponseMapper_PreservesExistingDateAndStatisticsWireShape()
    {
        var exitTime = new DateTime(2026, 8, 1, 20, 30, 0, DateTimeKind.Utc);
        var updatedAt = exitTime.AddDays(1);
        var snapshot = new PortfolioPerformanceSnapshot(
            1,
            1m,
            0.05m,
            0m,
            0m,
            [new PatternStatisticsSnapshot(
                PatternType.Breakout, "TQQQ", 1, 1m, 0.05m, 0m, 0m, updatedAt)],
            [new PortfolioEquityPoint(
                exitTime, "TQQQ", "Breakout", 50m, 0.05m, 50m)]);

        var response = PortfolioPerformanceResponse.Create(snapshot);

        response.PatternStats.Should().ContainSingle();
        response.PatternStats[0].LastUpdated.Should().Be(updatedAt.ToString("o"));
        response.EquityCurve.Should().ContainSingle();
        response.EquityCurve[0].Date.Should().Be("2026-08-01");
        response.EquityCurve[0].CumulativePnL.Should().Be(50m);
    }

    [Fact]
    public async Task Query_LoadsCompleteHistoryAndMapsPatternStatistics()
    {
        var tradeHistory = new Mock<ITradeHistoryStore>();
        tradeHistory.Setup(store => store.GetTradesAsync(
                null, null, null, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TradeRecord>
            {
                new()
                {
                    Id = 9,
                    Symbol = "TQQQ",
                    PatternType = PatternType.Breakout,
                    ExitTime = new DateTime(2026, 8, 1, 20, 0, 0, DateTimeKind.Utc),
                    PnL = 50m,
                    PnLPercent = 0.05m
                }
            });
        var statistics = new Mock<IPatternStatisticsQuery>();
        statistics.Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PatternStatisticsSnapshot>
            {
                new(
                    PatternType.Breakout,
                    "TQQQ",
                    4,
                    0.75m,
                    0.08m,
                    0.04m,
                    0.06m,
                    new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc))
            });
        var settings = new Mock<ISettingsRepository>();
        settings.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 10_000m });
        var sut = new PortfolioPerformanceQuery(
            tradeHistory.Object,
            statistics.Object,
            settings.Object);

        var result = await sut.GetAsync();

        result.TotalTrades.Should().Be(1);
        result.MaxDrawdown.Should().Be(0m);
        result.PatternStats.Should().ContainSingle(stat =>
            stat.PatternType == PatternType.Breakout
            && stat.Symbol == "TQQQ"
            && stat.Expectancy == 0.05m);
        tradeHistory.Verify(store => store.GetTradesAsync(
            null, null, null, 0, int.MaxValue, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PortfolioCompletedTrade Trade(
        long id,
        DateTime exitTime,
        decimal pnl,
        decimal pnlPercent) => new(
        id,
        $"SYM{id}",
        "Breakout",
        exitTime,
        pnl,
        pnlPercent,
        pnl > 0m);
}

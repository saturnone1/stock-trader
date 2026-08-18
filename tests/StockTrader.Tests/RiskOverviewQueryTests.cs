using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Risk;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Risk;

namespace StockTrader.Tests;

public class RiskOverviewQueryTests
{
    [Fact]
    public async Task GetAsync_ProjectsPositionRiskAndUsesOneObservationTime()
    {
        var observedAt = new DateTimeOffset(2026, 8, 18, 4, 30, 0, TimeSpan.Zero);
        var risk = new Mock<IRiskManagementService>();
        risk.Setup(service => service.GetCurrentRiskStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskState
            {
                DailyPnL = 125m,
                DailyPnLPercent = 0.00125m,
                OpenPositionCount = 2,
                LastUpdated = observedAt.UtcDateTime
            });
        var positions = new Mock<IOpenPositionQuery>();
        positions.Setup(query => query.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenPositionListSnapshot(
                [
                    Position(
                        "TQQQ", 100m, 106m, 97m, 10,
                        observedAt.UtcDateTime.AddDays(-4)),
                    Position(
                        "AAPL", 200m, 198m, 200m, 5,
                        observedAt.UtcDateTime.AddHours(1))
                ],
                50m,
                observedAt.UtcDateTime));
        var settings = new Mock<ISettingsRepository>();
        settings.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings
            {
                AccountSize = 100_000m,
                RiskPerTradePercent = 0.01m,
                DailyLossLimitPercent = 0.03m,
                MaxTotalPositions = 7,
                MaxPositionsPerSector = 2,
                MinExpectancy = 0.2m
            });
        var sut = new RiskOverviewQuery(
            risk.Object,
            positions.Object,
            settings.Object,
            Options.Create(new TradingSettings { MinConfidence = 0.4m }));

        var result = await sut.GetAsync();

        result.TotalUnrealizedPnL.Should().Be(50m);
        result.PositionRMultiples[0].RiskPerShare.Should().Be(3m);
        result.PositionRMultiples[0].RMultiple.Should().Be(2m);
        result.PositionRMultiples[0].HoldingDays.Should().Be(4);
        result.PositionRMultiples[1].RMultiple.Should().Be(0m);
        result.PositionRMultiples[1].HoldingDays.Should().Be(0);
        result.Settings.MinConfidence.Should().Be(0.4m);
    }

    [Theory]
    [InlineData(true, "2026-08-18T03:00:00Z", "2026-08-18T04:00:00Z", 60, true)]
    [InlineData(true, "2026-08-18T03:01:00Z", "2026-08-18T04:00:00Z", 60, false)]
    [InlineData(false, "2026-08-18T03:00:00Z", "2026-08-18T04:00:00Z", 60, false)]
    [InlineData(true, "2026-08-18T05:00:00Z", "2026-08-18T04:00:00Z", 60, false)]
    public void RiskAlertPolicy_RequiresHaltElapsedIntervalAndMonotonicClock(
        bool halted,
        string lastAlert,
        string observed,
        int intervalMinutes,
        bool expected)
    {
        RiskAlertPolicy.IsDue(
                halted,
                DateTime.Parse(lastAlert).ToUniversalTime(),
                DateTime.Parse(observed).ToUniversalTime(),
                TimeSpan.FromMinutes(intervalMinutes))
            .Should().Be(expected);
    }

    private static OpenPositionSnapshot Position(
        string symbol,
        decimal entryPrice,
        decimal currentPrice,
        decimal stopLossPrice,
        int quantity,
        DateTime openedAt) => new(
        0,
        symbol,
        string.Empty,
        quantity,
        entryPrice,
        currentPrice,
        stopLossPrice,
        0m,
        "GapUpPullback",
        (currentPrice - entryPrice) * quantity,
        0,
        0m,
        0m,
        0,
        openedAt,
        "Ready",
        null,
        null,
        null,
        false,
        0,
        0,
        false);
}

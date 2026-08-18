using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Risk;
using StockTrader.Application.Trading;
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
        var positions = new Mock<IOpenPositionStore>();
        positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new()
                {
                    Symbol = "TQQQ",
                    EntryPrice = 100m,
                    CurrentPrice = 106m,
                    StopLossPrice = 97m,
                    Quantity = 10,
                    OpenedAt = observedAt.UtcDateTime.AddDays(-4)
                },
                new()
                {
                    Symbol = "AAPL",
                    EntryPrice = 200m,
                    CurrentPrice = 198m,
                    StopLossPrice = 200m,
                    Quantity = 5,
                    OpenedAt = observedAt.UtcDateTime.AddHours(1)
                }
            });
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
            Options.Create(new TradingSettings { MinConfidence = 0.4m }),
            new FixedTimeProvider(observedAt));

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

    private sealed class FixedTimeProvider(DateTimeOffset observedAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => observedAt;
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Api.Contracts;
using StockTrader.Application.Accounts;
using StockTrader.Application.Dashboard;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Risk;
using StockTrader.Models;
using StockTrader.Services.Analysis;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Dashboard;

namespace StockTrader.Tests;

public sealed class DashboardQueryTests
{
    [Fact]
    public async Task ActiveBrokerAccountQueryMapsBrokerModelAndFailsUnavailableAccountToNull()
    {
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrokerAccount
            {
                AccountId = "paper-2",
                TotalEquity = 50_000m,
                Cash = 10_000m,
                BuyingPower = 20_000m,
                FetchedAt = Utc(11)
            });
        var manager = new Mock<IAccountManager>();
        manager.Setup(service => service.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(broker.Object);
        var sut = new ActiveBrokerAccountQuery(
            manager.Object,
            NullLogger<ActiveBrokerAccountQuery>.Instance);

        var snapshot = await sut.GetAsync();
        snapshot!.AccountId.Should().Be("paper-2");
        snapshot.TotalEquity.Should().Be(50_000m);

        manager.Setup(service => service.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"));
        (await sut.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ComposesExplicitSnapshotsWithoutInventingRiskMetrics()
    {
        var observedAt = Utc(12);
        var account = new Mock<IActiveBrokerAccountQuery>();
        account.Setup(query => query.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveBrokerAccountSnapshot(
                "paper-1", 101_000m, 40_000m, 80_000m, 900m, 250m,
                false, "OK", observedAt));
        var risk = new Mock<IRiskOverviewQuery>();
        risk.Setup(query => query.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Risk(observedAt));
        var activity = new Mock<IDashboardActivityStore>();
        activity.Setup(store => store.GetAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardActivitySnapshot(
                7,
                [new(1, "TQQQ", "Breakout", 100m, 97m, 106m, 2m, 0.1m, false, observedAt)]));
        var analysis = new Mock<IStockAnalysisService>();
        analysis.Setup(service => service.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime { RegimeLabel = "Bull" });
        var sut = new DashboardQuery(
            account.Object,
            risk.Object,
            activity.Object,
            analysis.Object);

        var result = await sut.GetAsync();
        var response = DashboardResponse.Create(result);

        result.Account!.TotalEquity.Should().Be(101_000m);
        result.Activity.ActiveSignalCount.Should().Be(7);
        result.MarketRegime.Should().Be("Bull");
        response.Risk.DailyPnLPercent.Should().Be(-0.0125m);
        response.Risk.TotalUnrealizedPnL.Should().Be(325m);
        response.Risk.IsTradingHalted.Should().BeTrue();
        response.OpenPositionCount.Should().Be(1);
        response.Positions.Should().ContainSingle()
            .Which.UnrealizedPnLPercent.Should().Be(0.05m);
        response.OrderMode.Should().Be("AlertOnly");
    }

    private static RiskOverviewSnapshot Risk(DateTime observedAt)
    {
        var positions = new OpenPositionListSnapshot(
            [new OpenPositionSnapshot(
                3, "TQQQ", "ETF", 10, 100m, 105m, 97m, 110m,
                "Breakout", 50m, 1, 106m, 2m, 2, Utc(10),
                "Ready", null, null, null, false, 0, 0, false)],
            325m,
            observedAt);
        return new RiskOverviewSnapshot(
            new RiskStateSnapshot(
                -1_250m, -0.0125m, true, 1,
                new Dictionary<string, int> { ["ETF"] = 1 }, observedAt),
            new RiskSettingsSnapshot(
                100_000m, 0.01m, 0.03m, 5, 2, 0.1m, 0.5m,
                OrderMode.AlertOnly),
            [],
            325m,
            positions);
    }

    private static DateTime Utc(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);
}

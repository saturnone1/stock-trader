using FluentAssertions;
using Moq;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public class LivePositionExitCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitAsync_DoesNotCallBrokerWhenAnotherWorkerOwnsClaim()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExitAsync(1, It.IsAny<DateTime>(), "손절", default))
            .ReturnsAsync(false);
        var broker = new Mock<IBrokerService>();
        var coordinator = Create(trades);

        var result = await coordinator.SubmitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LiveExitSubmissionStatus.AlreadyPending);
        broker.Verify(service => service.ClosePositionAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_PersistsBrokerOrderIdAfterClaim()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExitAsync(1, Now.UtcDateTime, "손절", default))
            .ReturnsAsync(true);
        trades.Setup(repo => repo.SetPositionExitOrderIdAsync(1, Now.UtcDateTime, "exit-1", default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", default)).ReturnsAsync(new BrokerOrder
        {
            OrderId = "exit-1",
            Symbol = "TQQQ",
            Direction = TradeDirection.Short,
            Status = BrokerOrderStatus.Accepted,
            SubmittedAt = Now.UtcDateTime,
        });

        var result = await Create(trades).SubmitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LiveExitSubmissionStatus.Accepted);
        result.Order!.OrderId.Should().Be("exit-1");
    }

    [Fact]
    public async Task SubmitAsync_ReleasesClaimWhenBrokerExplicitlyRejectsSubmission()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExitAsync(1, Now.UtcDateTime, "손절", default))
            .ReturnsAsync(true);
        trades.Setup(repo => repo.ReleasePositionExitClaimAsync(1, Now.UtcDateTime, default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", default))
            .ReturnsAsync((BrokerOrder?)null);

        var result = await Create(trades).SubmitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LiveExitSubmissionStatus.Failed);
        trades.Verify(repo => repo.ReleasePositionExitClaimAsync(1, Now.UtcDateTime, default), Times.Once);
    }

    private static LivePositionExitCoordinator Create(Mock<ITradeRepository> trades) =>
        new(trades.Object, new FixedTimeProvider(Now));

    private static Position Position() => new() { Id = 1, Symbol = "TQQQ" };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

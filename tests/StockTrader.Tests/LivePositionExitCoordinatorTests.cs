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
        result.BrokerOrderIdPersisted.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_KeepsClaimWhenBrokerOrderIdPersistenceLosesRace()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExitAsync(1, Now.UtcDateTime, "손절", default))
            .ReturnsAsync(true);
        trades.Setup(repo => repo.SetPositionExitOrderIdAsync(1, Now.UtcDateTime, "exit-1", default))
            .ReturnsAsync(false);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", default))
            .ReturnsAsync(ExitOrder(BrokerOrderStatus.Accepted));
        var position = Position();

        var result = await Create(trades).SubmitAsync(position, "손절", broker.Object);

        result.Status.Should().Be(LiveExitSubmissionStatus.Accepted);
        result.BrokerOrderIdPersisted.Should().BeFalse();
        position.ExitRequestedAt.Should().Be(Now.UtcDateTime);
        position.ExitOrderId.Should().BeNull();
        trades.Verify(repo => repo.ReleasePositionExitClaimAsync(
            It.IsAny<long>(), It.IsAny<DateTime>(), default), Times.Never);
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

    [Fact]
    public async Task ReconcileAsync_WaitsWithoutChangingClaimWhenBrokerHasNoProof()
    {
        var trades = new Mock<ITradeRepository>();
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.GetOrderHistoryAsync(
                Now.UtcDateTime.AddSeconds(-2), Now.UtcDateTime.AddSeconds(1), default))
            .ReturnsAsync([]);
        var position = PendingPosition();

        var result = await Create(trades).ReconcileAsync(position, broker.Object);

        result.Status.Should().Be(LiveExitReconciliationStatus.AwaitingBroker);
        trades.Verify(repo => repo.ReleasePositionExitClaimAsync(
            It.IsAny<long>(), It.IsAny<DateTime>(), default), Times.Never);
        trades.Verify(repo => repo.TryCompletePositionExitAsync(
            It.IsAny<Position>(), It.IsAny<TradeRecord>(), default), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ReleasesOnlyTerminallyFailedOrder()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.ReleasePositionExitClaimAsync(1, Now.UtcDateTime, default))
            .ReturnsAsync(true);
        var position = PendingPosition();

        var result = await Create(trades).ReconcileAsync(position, Mock.Of<IBrokerService>(),
            [ExitOrder(BrokerOrderStatus.Rejected)]);

        result.Status.Should().Be(LiveExitReconciliationStatus.ReleasedForRetry);
        position.ExitRequestedAt.Should().BeNull();
        position.ExitRequestReason.Should().BeNull();
        position.ExitOrderId.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileAsync_CompletesPositionAndTradeFromProvenFill()
    {
        var trades = new Mock<ITradeRepository>();
        TradeRecord? savedTrade = null;
        trades.Setup(repo => repo.TryCompletePositionExitAsync(
                It.IsAny<Position>(), It.IsAny<TradeRecord>(), default))
            .Callback<Position, TradeRecord, CancellationToken>((_, trade, _) => savedTrade = trade)
            .ReturnsAsync(true);
        var position = PendingPosition();
        position.EntryPrice = 50m;
        position.Quantity = 10;
        position.OpenedAt = Now.UtcDateTime.AddDays(-2);

        var result = await Create(trades).ReconcileAsync(position, Mock.Of<IBrokerService>(),
            [ExitOrder(BrokerOrderStatus.Filled, 48m)]);

        result.Status.Should().Be(LiveExitReconciliationStatus.Completed);
        position.ClosedAt.Should().Be(Now.UtcDateTime.AddSeconds(1));
        position.ExitPrice.Should().Be(48m);
        savedTrade.Should().NotBeNull();
        savedTrade!.PnL.Should().Be(-20m);
        savedTrade.ExitReason.Should().Be("손절");
    }

    private static LivePositionExitCoordinator Create(Mock<ITradeRepository> trades) =>
        new(trades.Object, new FixedTimeProvider(Now));

    private static Position Position() => new() { Id = 1, Symbol = "TQQQ" };

    private static Position PendingPosition() => new()
    {
        Id = 1,
        Symbol = "TQQQ",
        ExitRequestedAt = Now.UtcDateTime,
        ExitRequestReason = "손절",
        ExitOrderId = "exit-1"
    };

    private static BrokerOrder ExitOrder(
        BrokerOrderStatus status,
        decimal? fillPrice = null) => new()
    {
        OrderId = "exit-1",
        Symbol = "TQQQ",
        Direction = TradeDirection.Short,
        Status = status,
        AverageFillPrice = fillPrice,
        SubmittedAt = Now.UtcDateTime,
        FilledAt = status == BrokerOrderStatus.Filled ? Now.UtcDateTime.AddSeconds(1) : null
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

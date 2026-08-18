using FluentAssertions;
using Moq;
using StockTrader.Application.Execution;
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
        trades.Setup(repo => repo.TryClaimPositionExitAsync(
                It.Is<PositionExitClaim>(claim => claim.PositionId == 1 && claim.Reason == "손절"),
                default))
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
        trades.Setup(repo => repo.TryClaimPositionExitAsync(
                It.Is<PositionExitClaim>(claim => claim.PositionId == 1
                    && claim.RequestedAt == Now.UtcDateTime
                    && claim.Quantity == 10), default))
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
        trades.Setup(repo => repo.TryClaimPositionExitAsync(
                It.Is<PositionExitClaim>(claim => claim.PositionId == 1
                    && claim.RequestedAt == Now.UtcDateTime), default))
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
        trades.Setup(repo => repo.TryClaimPositionExitAsync(
                It.Is<PositionExitClaim>(claim => claim.PositionId == 1
                    && claim.RequestedAt == Now.UtcDateTime), default))
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
        trades.Verify(repo => repo.TryApplyPositionExitFillAsync(
            It.IsAny<PositionExitFill>(), It.IsAny<TradeRecord>(), default), Times.Never);
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
        trades.Setup(repo => repo.TryApplyPositionExitFillAsync(
                It.IsAny<PositionExitFill>(), It.IsAny<TradeRecord>(), default))
            .Callback<PositionExitFill, TradeRecord, CancellationToken>(
                (_, trade, _) => savedTrade = trade)
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

    [Fact]
    public async Task SubmitAsync_PartialExitPersistsQuantityAndUsesQuantityBrokerContract()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExitAsync(
                It.Is<PositionExitClaim>(claim => claim.Quantity == 4
                    && claim.MarksPartialProfit), default))
            .ReturnsAsync(true);
        trades.Setup(repo => repo.SetPositionExitOrderIdAsync(1, Now.UtcDateTime, "exit-1", default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", 4, default))
            .ReturnsAsync(ExitOrder(BrokerOrderStatus.Accepted, quantity: 4));
        var position = Position();

        var result = await Create(trades).SubmitAsync(
            position,
            new LivePositionExitRequest(4, "1차 이익실현", true),
            broker.Object);

        result.Status.Should().Be(LiveExitSubmissionStatus.Accepted);
        position.ExitRequestQuantity.Should().Be(4);
        position.ExitRequestMarksPartialProfit.Should().BeTrue();
        broker.Verify(service => service.ClosePositionAsync("TQQQ", 4, default), Times.Once);
        broker.Verify(service => service.ClosePositionAsync("TQQQ", default), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_PartialFillKeepsPositionOpenAndRecordsExecutedQuantity()
    {
        var trades = new Mock<ITradeRepository>();
        PositionExitFill? savedFill = null;
        TradeRecord? savedTrade = null;
        trades.Setup(repo => repo.TryApplyPositionExitFillAsync(
                It.IsAny<PositionExitFill>(), It.IsAny<TradeRecord>(), default))
            .Callback<PositionExitFill, TradeRecord, CancellationToken>((fill, trade, _) =>
            {
                savedFill = fill;
                savedTrade = trade;
            })
            .ReturnsAsync(true);
        var position = PendingPosition();
        position.Quantity = 10;
        position.InitialQuantity = 10;
        position.EntryPrice = 50m;
        position.ExitRequestQuantity = 4;
        position.ExitRequestMarksPartialProfit = true;

        var result = await Create(trades).ReconcileAsync(position, Mock.Of<IBrokerService>(),
            [ExitOrder(BrokerOrderStatus.Filled, 55m, 4, 4)]);

        result.Status.Should().Be(LiveExitReconciliationStatus.Completed);
        result.FilledQuantity.Should().Be(4);
        result.IsFullExit.Should().BeFalse();
        position.Quantity.Should().Be(6);
        position.ClosedAt.Should().BeNull();
        position.PartialProfitTaken.Should().BeTrue();
        position.StopLossPrice.Should().Be(50m);
        position.BreakevenApplied.Should().BeTrue();
        position.ExitRequestedAt.Should().BeNull();
        savedFill!.ExpectedPositionQuantity.Should().Be(10);
        savedTrade!.Quantity.Should().Be(4);
        savedTrade.PnL.Should().Be(20m);
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotApplyMismatchedTerminalFillQuantity()
    {
        var trades = new Mock<ITradeRepository>();
        var position = PendingPosition();
        position.Quantity = 10;
        position.ExitRequestQuantity = 4;

        var result = await Create(trades).ReconcileAsync(position, Mock.Of<IBrokerService>(),
            [ExitOrder(BrokerOrderStatus.Filled, 55m, 4, 3)]);

        result.Status.Should().Be(LiveExitReconciliationStatus.BrokerFillMismatch);
        result.FilledQuantity.Should().Be(3);
        trades.Verify(repo => repo.TryApplyPositionExitFillAsync(
            It.IsAny<PositionExitFill>(), It.IsAny<TradeRecord>(), default), Times.Never);
    }

    private static LivePositionExitCoordinator Create(Mock<ITradeRepository> trades) =>
        new(trades.Object, new FixedTimeProvider(Now));

    private static Position Position() => new()
    {
        Id = 1,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
    };

    private static Position PendingPosition() => new()
    {
        Id = 1,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
        ExitRequestedAt = Now.UtcDateTime,
        ExitRequestReason = "손절",
        ExitRequestQuantity = 10,
        ExitOrderId = "exit-1"
    };

    private static BrokerOrder ExitOrder(
        BrokerOrderStatus status,
        decimal? fillPrice = null,
        int quantity = 0,
        int filledQuantity = 0) => new()
        {
            OrderId = "exit-1",
            Symbol = "TQQQ",
            Direction = TradeDirection.Short,
            Quantity = quantity,
            FilledQuantity = filledQuantity,
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

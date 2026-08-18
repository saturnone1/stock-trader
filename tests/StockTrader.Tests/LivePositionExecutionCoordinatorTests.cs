using FluentAssertions;
using Moq;
using StockTrader.Application.Execution;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public class LivePositionExecutionCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitFullExitAsync_DoesNotCallBrokerWhenAnotherWorkerOwnsClaim()
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExecutionAsync(
                It.IsAny<PositionExecutionClaim>(), default))
            .ReturnsAsync(false);
        var broker = new Mock<IBrokerService>();

        var result = await Create(trades).SubmitFullExitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.AlreadyPending);
        broker.Verify(service => service.ClosePositionAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task UnsupportedBrokerDoesNotClaimOrSubmitPositionOrder()
    {
        var trades = new Mock<ITradeRepository>();
        var broker = new Mock<IBrokerService>();
        broker.SetupGet(item => item.BrokerType).Returns(BrokerType.KoreaInvestment);

        var result = await Create(trades).SubmitFullExitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Unsupported);
        trades.Verify(item => item.TryClaimPositionExecutionAsync(
            It.IsAny<PositionExecutionClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        broker.Verify(item => item.ClosePositionAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitFullExitAsync_PersistsTrackableBrokerOrder()
    {
        var trades = ClaimingRepository(PositionExecutionKind.FullExit, 10);
        trades.Setup(repo => repo.SetPositionExecutionOrderIdAsync(
                1, Now.UtcDateTime, "order-1", default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", default))
            .ReturnsAsync(Order(TradeDirection.Short, BrokerOrderStatus.Accepted, 10));

        var result = await Create(trades).SubmitFullExitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Accepted);
        result.BrokerOrderIdPersisted.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_ScaleInUsesTrackableBuyOrderAndPersistsRule()
    {
        var trades = ClaimingRepository(PositionExecutionKind.ScaleIn, 3, ruleIndex: 2);
        trades.Setup(repo => repo.SetPositionExecutionOrderIdAsync(
                1, Now.UtcDateTime, "order-1", default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.IncreasePositionAsync("TQQQ", 3, default))
            .ReturnsAsync(Order(TradeDirection.Long, BrokerOrderStatus.Accepted, 3));
        var position = Position();

        var result = await Create(trades).SubmitAsync(
            position,
            new LivePositionExecutionRequest(
                3, "추가 매수(30%)", PositionExecutionKind.ScaleIn, ScalingRuleIndex: 2),
            broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Accepted);
        position.ExecutionRequestKind.Should().Be(PositionExecutionKind.ScaleIn);
        position.ExecutionRequestRuleIndex.Should().Be(2);
        broker.Verify(service => service.ClosePositionAsync(
            It.IsAny<string>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_ScaleOutUsesQuantitySellOrder()
    {
        var trades = ClaimingRepository(PositionExecutionKind.ScaleOut, 4, ruleIndex: 1);
        trades.Setup(repo => repo.SetPositionExecutionOrderIdAsync(
                1, Now.UtcDateTime, "order-1", default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.ClosePositionAsync("TQQQ", 4, default))
            .ReturnsAsync(Order(TradeDirection.Short, BrokerOrderStatus.Accepted, 4));

        var result = await Create(trades).SubmitAsync(
            Position(),
            new LivePositionExecutionRequest(
                4, "분할 매도(40%)", PositionExecutionKind.ScaleOut, ScalingRuleIndex: 1),
            broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Accepted);
        broker.Verify(service => service.ClosePositionAsync("TQQQ", 4, default), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_ReleasesClaimWhenBrokerRejectsSubmission()
    {
        var trades = ClaimingRepository(PositionExecutionKind.ScaleIn, 3, ruleIndex: 0);
        trades.Setup(repo => repo.ReleasePositionExecutionClaimAsync(1, Now.UtcDateTime, default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.IncreasePositionAsync("TQQQ", 3, default))
            .ReturnsAsync((BrokerOrder?)null);

        var result = await Create(trades).SubmitAsync(
            Position(),
            new LivePositionExecutionRequest(
                3, "추가 매수", PositionExecutionKind.ScaleIn, ScalingRuleIndex: 0),
            broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Failed);
        trades.Verify(repo => repo.ReleasePositionExecutionClaimAsync(
            1, Now.UtcDateTime, default), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_ReleasesClaimWhenBrokerReturnsMismatchedOrder()
    {
        var trades = ClaimingRepository(PositionExecutionKind.ScaleIn, 3, ruleIndex: 0);
        trades.Setup(repo => repo.ReleasePositionExecutionClaimAsync(1, Now.UtcDateTime, default))
            .ReturnsAsync(true);
        var broker = new Mock<IBrokerService>();
        broker.Setup(service => service.IncreasePositionAsync("TQQQ", 3, default))
            .ReturnsAsync(Order(TradeDirection.Short, BrokerOrderStatus.Accepted, 3));

        var result = await Create(trades).SubmitAsync(
            Position(),
            new LivePositionExecutionRequest(
                3, "추가 매수", PositionExecutionKind.ScaleIn, ScalingRuleIndex: 0),
            broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Failed);
        trades.Verify(repo => repo.ReleasePositionExecutionClaimAsync(
            1, Now.UtcDateTime, default), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotReportMissingBrokerOrderIdAsPersisted()
    {
        var trades = ClaimingRepository(PositionExecutionKind.FullExit, 10);
        var broker = new Mock<IBrokerService>();
        var order = Order(TradeDirection.Short, BrokerOrderStatus.Accepted, 10);
        order.OrderId = string.Empty;
        broker.Setup(service => service.ClosePositionAsync("TQQQ", default))
            .ReturnsAsync(order);

        var result = await Create(trades).SubmitFullExitAsync(Position(), "손절", broker.Object);

        result.Status.Should().Be(LivePositionExecutionSubmissionStatus.Accepted);
        result.BrokerOrderIdPersisted.Should().BeFalse();
        trades.Verify(repo => repo.SetPositionExecutionOrderIdAsync(
            It.IsAny<long>(), It.IsAny<DateTime>(), It.IsAny<string?>(), default), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ScaleInAppliesWeightedAverageAndCountAfterProvenFill()
    {
        var trades = new Mock<ITradeRepository>();
        PositionExecutionFill? savedFill = null;
        trades.Setup(repo => repo.TryApplyPositionExecutionFillAsync(
                It.IsAny<PositionExecutionFill>(), null, default))
            .Callback<PositionExecutionFill, TradeRecord?, CancellationToken>(
                (fill, _, _) => savedFill = fill)
            .ReturnsAsync(true);
        var position = Pending(PositionExecutionKind.ScaleIn, quantity: 4, ruleIndex: 2);
        position.EntryPrice = 50m;
        position.Quantity = 10;

        var result = await Create(trades).ReconcileAsync(
            position,
            Mock.Of<IBrokerService>(),
            [Order(TradeDirection.Long, BrokerOrderStatus.Filled, 4, 55m)]);

        result.Status.Should().Be(LivePositionExecutionReconciliationStatus.Completed);
        position.Quantity.Should().Be(14);
        position.EntryPrice.Should().BeApproximately(51.428571m, 0.000001m);
        position.ScalingExecutionCounts[2].Should().Be(1);
        savedFill!.Kind.Should().Be(PositionExecutionKind.ScaleIn);
    }

    [Fact]
    public async Task ReconcileAsync_ScaleOutReducesQuantityAndCreatesRealizedTrade()
    {
        var trades = new Mock<ITradeRepository>();
        TradeRecord? savedTrade = null;
        trades.Setup(repo => repo.TryApplyPositionExecutionFillAsync(
                It.IsAny<PositionExecutionFill>(), It.IsAny<TradeRecord>(), default))
            .Callback<PositionExecutionFill, TradeRecord?, CancellationToken>(
                (_, trade, _) => savedTrade = trade)
            .ReturnsAsync(true);
        var position = Pending(PositionExecutionKind.ScaleOut, quantity: 4, ruleIndex: 1);
        position.EntryPrice = 50m;

        var result = await Create(trades).ReconcileAsync(
            position,
            Mock.Of<IBrokerService>(),
            [Order(TradeDirection.Short, BrokerOrderStatus.Filled, 4, 55m)]);

        result.Status.Should().Be(LivePositionExecutionReconciliationStatus.Completed);
        position.Quantity.Should().Be(6);
        position.ScalingExecutionCounts[1].Should().Be(1);
        savedTrade!.PnL.Should().Be(20m);
        savedTrade.ExitReason.Should().Be("분할 매도");
    }

    [Fact]
    public async Task ReconcileAsync_RejectsWrongDirectionAndMismatchedFillQuantity()
    {
        var trades = new Mock<ITradeRepository>();
        var position = Pending(PositionExecutionKind.ScaleIn, quantity: 4, ruleIndex: 0);
        var coordinator = Create(trades);

        var wrongDirection = await coordinator.ReconcileAsync(
            position,
            Mock.Of<IBrokerService>(),
            [Order(TradeDirection.Short, BrokerOrderStatus.Filled, 4, 55m)]);
        var wrongQuantity = await coordinator.ReconcileAsync(
            position,
            Mock.Of<IBrokerService>(),
            [Order(TradeDirection.Long, BrokerOrderStatus.Filled, 3, 55m)]);

        wrongDirection.Status.Should().Be(LivePositionExecutionReconciliationStatus.AwaitingBroker);
        wrongQuantity.Status.Should().Be(
            LivePositionExecutionReconciliationStatus.BrokerFillMismatch);
        trades.Verify(repo => repo.TryApplyPositionExecutionFillAsync(
            It.IsAny<PositionExecutionFill>(), It.IsAny<TradeRecord?>(), default), Times.Never);
    }

    private static Mock<ITradeRepository> ClaimingRepository(
        PositionExecutionKind kind,
        int quantity,
        int? ruleIndex = null)
    {
        var trades = new Mock<ITradeRepository>();
        trades.Setup(repo => repo.TryClaimPositionExecutionAsync(
                It.Is<PositionExecutionClaim>(claim =>
                    claim.Kind == kind
                    && claim.Quantity == quantity
                    && claim.ScalingRuleIndex == ruleIndex),
                default))
            .ReturnsAsync(true);
        return trades;
    }

    private static LivePositionExecutionCoordinator Create(Mock<ITradeRepository> trades) =>
        new(trades.Object, new FixedTimeProvider(Now));

    private static Position Position() => new()
    {
        Id = 1,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
        EntryPrice = 50m,
        StopLossPrice = 45m,
    };

    private static Position Pending(
        PositionExecutionKind kind,
        int quantity,
        int? ruleIndex = null)
    {
        var position = Position();
        position.ExecutionRequestedAt = Now.UtcDateTime;
        position.ExecutionRequestReason = kind == PositionExecutionKind.ScaleOut
            ? "분할 매도"
            : "추가 매수";
        position.ExecutionRequestQuantity = quantity;
        position.ExecutionRequestKind = kind;
        position.ExecutionRequestRuleIndex = ruleIndex;
        position.ExecutionOrderId = "order-1";
        return position;
    }

    private static BrokerOrder Order(
        TradeDirection direction,
        BrokerOrderStatus status,
        int quantity,
        decimal? fillPrice = null) => new()
        {
            OrderId = "order-1",
            Symbol = "TQQQ",
            Direction = direction,
            Quantity = quantity,
            FilledQuantity = status == BrokerOrderStatus.Filled ? quantity : 0,
            Status = status,
            AverageFillPrice = fillPrice,
            SubmittedAt = Now.UtcDateTime,
            FilledAt = status == BrokerOrderStatus.Filled ? Now.UtcDateTime.AddSeconds(1) : null,
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

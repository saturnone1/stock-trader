using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.Accounts;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public sealed class LiveEntryExecutionCoordinatorTests
{
    private static readonly DateTime Now =
        new(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AcceptedEntryUsesTheAccountSnapshotForDurableOwnership()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedOrder());
        broker.Setup(item => item.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Position
            {
                Symbol = "TQQQ",
                Quantity = 6,
                EntryPrice = 102m,
                CurrentPrice = 103m
            }]);
        Position? committed = null;
        store.Setup(item => item.CommitAcceptedEntryAsync(
                recommendation, It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<TradeRecommendation, Position, CancellationToken>(
                (_, position, _) => committed = position)
            .Returns(Task.CompletedTask);
        var coordinator = Create(store, broker);

        var result = await coordinator.ExecuteAsync(
            recommendation,
            Context(accountId: 17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.Completed);
        result.Order!.OrderId.Should().Be("broker-entry-1");
        committed.Should().NotBeNull();
        committed!.AccountId.Should().Be(17);
        committed.Quantity.Should().Be(6);
        committed.EntryPrice.Should().Be(102m);
        committed.StopLossPrice.Should().Be(97m);
        committed.TargetPrice.Should().Be(112m);
        committed.OpenedAt.Should().Be(Now);
    }

    [Fact]
    public async Task RejectedEntryDoesNotCreateLocalExecutionState()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BrokerOrder?)null);
        var coordinator = Create(store, broker);

        var result = await coordinator.ExecuteAsync(
            recommendation,
            Context(accountId: 17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.Rejected);
        result.BrokerAccepted.Should().BeFalse();
        store.Verify(item => item.CommitAcceptedEntryAsync(
            It.IsAny<TradeRecommendation>(),
            It.IsAny<Position>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptedOrderWithPersistenceFailureRetainsEvidenceAndMustNotLookRejected()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedOrder());
        broker.Setup(item => item.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(item => item.CommitAcceptedEntryAsync(
                recommendation, It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var coordinator = Create(store, broker);

        var result = await coordinator.ExecuteAsync(
            recommendation,
            Context(accountId: 17, broker.Object));

        result.Status.Should().Be(
            LiveEntryExecutionStatus.BrokerAcceptedTrackingFailed);
        result.BrokerAccepted.Should().BeTrue();
        result.IsTracked.Should().BeFalse();
        result.Order!.OrderId.Should().Be("broker-entry-1");
        result.Error.Should().Contain("database unavailable");
    }

    [Fact]
    public async Task TerminalBrokerRejectionIsNotTreatedAsAcceptance()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedOrder(status: BrokerOrderStatus.Rejected));
        var coordinator = Create(store, broker);

        var result = await coordinator.ExecuteAsync(
            recommendation,
            Context(accountId: 17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.Rejected);
        result.BrokerAccepted.Should().BeFalse();
        result.Order.Should().NotBeNull();
        store.Verify(item => item.CommitAcceptedEntryAsync(
            It.IsAny<TradeRecommendation>(),
            It.IsAny<Position>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("SQQQ", TradeDirection.Long, 10, "symbol")]
    [InlineData("TQQQ", TradeDirection.Short, 10, "direction")]
    [InlineData("TQQQ", TradeDirection.Long, 9, "quantity")]
    public async Task AcceptedButMismatchedEvidenceFailsClosedWithoutLookingRejected(
        string symbol,
        TradeDirection direction,
        int quantity,
        string expectedError)
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AcceptedOrder(symbol, direction, quantity));
        var coordinator = Create(store, broker);

        var result = await coordinator.ExecuteAsync(
            recommendation,
            Context(accountId: 17, broker.Object));

        result.Status.Should().Be(
            LiveEntryExecutionStatus.BrokerAcceptedTrackingFailed);
        result.BrokerAccepted.Should().BeTrue();
        result.Error.Should().Contain(expectedError);
        broker.Verify(item => item.GetPositionsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        store.Verify(item => item.CommitAcceptedEntryAsync(
            It.IsAny<TradeRecommendation>(),
            It.IsAny<Position>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static LiveEntryExecutionCoordinator Create(
        Mock<ILiveEntryExecutionStore> store,
        Mock<IBrokerService> broker) => new(
            store.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)),
            NullLogger<LiveEntryExecutionCoordinator>.Instance);

    private static AccountBrokerContext Context(int accountId, IBrokerService broker) => new(
        new ManagedTradingAccount
        {
            Id = accountId,
            AccountName = "Paper",
            IsActive = true,
            IsEnabled = true
        },
        broker);

    private static TradeRecommendation Recommendation() => new()
    {
        Id = 3,
        Symbol = "TQQQ",
        PatternType = PatternType.GapUpPullback,
        GeneratedAt = Now,
        EntryPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
        PositionSize = 1_000m,
        ShareQuantity = 10,
        Mode = OrderMode.AutoOrder
    };

    private static BrokerOrder AcceptedOrder(
        string symbol = "TQQQ",
        TradeDirection direction = TradeDirection.Long,
        int quantity = 10,
        BrokerOrderStatus status = BrokerOrderStatus.Accepted) => new()
    {
        OrderId = "broker-entry-1",
        Symbol = symbol,
        Direction = direction,
        Quantity = quantity,
        Status = status,
        OrderType = BrokerOrderType.BracketOrder,
        SubmittedAt = Now
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

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
    public async Task FilledEntryIsClaimedBeforeSubmissionAndCommittedWithExactFill()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = ClaimingStore(recommendation);
        var sequence = new List<string>();
        store.Setup(item => item.TryClaimAsync(
                recommendation, 17, Now, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("claim"))
            .ReturnsAsync(true);
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("submit"))
            .ReturnsAsync(Order(BrokerOrderStatus.Filled, fillPrice: 102m));
        Position? committed = null;
        store.Setup(item => item.CommitFilledEntryAsync(
                recommendation, Now, It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<TradeRecommendation, DateTime, Position, CancellationToken>(
                (_, _, position, _) => committed = position)
            .ReturnsAsync(true);

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object));

        sequence.Should().Equal("claim", "submit");
        result.Status.Should().Be(LiveEntryExecutionStatus.Completed);
        committed!.AccountId.Should().Be(17);
        committed.Quantity.Should().Be(10);
        committed.EntryPrice.Should().Be(102m);
        committed.StopLossPrice.Should().Be(97m);
        committed.TargetPrice.Should().Be(112m);
        recommendation.WasExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentClaimPreventsBrokerSubmission()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();
        store.Setup(item => item.TryClaimAsync(
                recommendation, 17, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.AlreadyPending);
        broker.Verify(item => item.SubmitEntryOrderAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnsupportedBrokerIsRejectedBeforeDurableClaim()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = new Mock<ILiveEntryExecutionStore>();

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object, BrokerType.LsSecurities));

        result.Status.Should().Be(LiveEntryExecutionStatus.Unsupported);
        result.ShouldPreventRetry.Should().BeFalse();
        store.Verify(item => item.TryClaimAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<int>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
        broker.Verify(item => item.SubmitEntryOrderAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmissionExceptionRetainsClaimAndDoesNotPermitRetry()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = ClaimingStore(recommendation);
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("connection dropped"));

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.SubmissionUnconfirmed);
        recommendation.EntryRequestedAt.Should().Be(Now);
        store.Verify(item => item.ReleaseClaimAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExplicitNullSubmissionReleasesClaimForRetry()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = ClaimingStore(recommendation);
        store.Setup(item => item.ReleaseClaimAsync(
                recommendation, Now, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BrokerOrder?)null);

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.Rejected);
        recommendation.EntryRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task AcceptedPendingOrderPersistsEvidenceAndAwaitsBroker()
    {
        var recommendation = Recommendation();
        var broker = new Mock<IBrokerService>();
        var store = ClaimingStore(recommendation);
        broker.Setup(item => item.SubmitEntryOrderAsync(
                recommendation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Order(BrokerOrderStatus.Accepted));

        var result = await Create(store).ExecuteAsync(
            recommendation,
            Context(17, broker.Object));

        result.Status.Should().Be(LiveEntryExecutionStatus.AwaitingBroker);
        recommendation.EntryOrderId.Should().Be("broker-entry-1");
        store.Verify(item => item.SetOrderEvidenceAsync(
            recommendation, Now, "broker-entry-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestartReconciliationWithoutStoredIdFinalizesOnlyUniqueExactOrder()
    {
        var recommendation = PendingRecommendation(orderId: null);
        var store = new Mock<ILiveEntryExecutionStore>();
        store.Setup(item => item.CommitFilledEntryAsync(
                recommendation, Now, It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var broker = Mock.Of<IBrokerService>();

        var result = await Create(store).ReconcileAsync(
            recommendation,
            Context(17, broker),
            [Order(BrokerOrderStatus.Filled, fillPrice: 101m)]);

        result.Status.Should().Be(LiveEntryExecutionStatus.Completed);
        result.Position!.EntryPrice.Should().Be(101m);
    }

    [Fact]
    public async Task RestartReconciliationFailsClosedOnAmbiguousOrders()
    {
        var recommendation = PendingRecommendation(orderId: null);
        var store = new Mock<ILiveEntryExecutionStore>();

        var result = await Create(store).ReconcileAsync(
            recommendation,
            Context(17, Mock.Of<IBrokerService>()),
            [
                Order(BrokerOrderStatus.Filled, "entry-1", 101m),
                Order(BrokerOrderStatus.Filled, "entry-2", 102m),
            ]);

        result.Status.Should().Be(LiveEntryExecutionStatus.AmbiguousEvidence);
        store.Verify(item => item.CommitFilledEntryAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<DateTime>(),
            It.IsAny<Position>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(item => item.ReleaseClaimAsync(
            It.IsAny<TradeRecommendation>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvenTerminalOrderReleasesClaimAfterRestart()
    {
        var recommendation = PendingRecommendation("broker-entry-1");
        var store = new Mock<ILiveEntryExecutionStore>();
        store.Setup(item => item.ReleaseClaimAsync(
                recommendation, Now, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Create(store).ReconcileAsync(
            recommendation,
            Context(17, Mock.Of<IBrokerService>()),
            [Order(BrokerOrderStatus.Rejected)]);

        result.Status.Should().Be(LiveEntryExecutionStatus.Rejected);
        recommendation.EntryRequestedAt.Should().BeNull();
    }

    private static Mock<ILiveEntryExecutionStore> ClaimingStore(
        TradeRecommendation recommendation)
    {
        var store = new Mock<ILiveEntryExecutionStore>();
        store.Setup(item => item.TryClaimAsync(
                recommendation, 17, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        store.Setup(item => item.SetOrderEvidenceAsync(
                recommendation, Now, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return store;
    }

    private static LiveEntryExecutionCoordinator Create(
        Mock<ILiveEntryExecutionStore> store) => new(
            store.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)),
            NullLogger<LiveEntryExecutionCoordinator>.Instance);

    private static AccountBrokerContext Context(
        int accountId,
        IBrokerService broker,
        BrokerType brokerType = BrokerType.Alpaca) => new(
        new ManagedTradingAccount
        {
            Id = accountId,
            AccountName = "Paper",
            IsActive = true,
            IsEnabled = true,
            BrokerType = brokerType,
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
        Mode = OrderMode.AutoOrder,
    };

    private static TradeRecommendation PendingRecommendation(string? orderId)
    {
        var recommendation = Recommendation();
        recommendation.EntryRequestedAt = Now;
        recommendation.EntryAccountId = 17;
        recommendation.EntryOrderId = orderId;
        return recommendation;
    }

    private static BrokerOrder Order(
        BrokerOrderStatus status,
        string orderId = "broker-entry-1",
        decimal? fillPrice = null) => new()
        {
            OrderId = orderId,
            Symbol = "TQQQ",
            Direction = TradeDirection.Long,
            Quantity = 10,
            FilledQuantity = status == BrokerOrderStatus.Filled ? 10 : 0,
            AverageFillPrice = fillPrice,
            Status = status,
            OrderType = BrokerOrderType.BracketOrder,
            SubmittedAt = Now,
            FilledAt = status == BrokerOrderStatus.Filled ? Now.AddSeconds(1) : null,
        };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

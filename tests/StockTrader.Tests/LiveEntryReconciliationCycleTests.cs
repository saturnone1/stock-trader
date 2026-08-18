using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Accounts;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public sealed class LiveEntryReconciliationCycleTests
{
    private readonly Mock<ILiveEntryExecutionStore> _store = new();
    private readonly Mock<ILiveEntryExecutionCoordinator> _coordinator = new();
    private readonly Mock<IAccountManager> _accounts = new();
    private readonly DateTimeOffset _now =
        new(2026, 8, 18, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task PendingEntriesUseOnlyTheirOwningAccountsOrderEvidence()
    {
        var first = Recommendation(1, 11, _now.UtcDateTime.AddMinutes(-5));
        var earlier = Recommendation(2, 11, _now.UtcDateTime.AddMinutes(-8));
        var second = Recommendation(3, 22, _now.UtcDateTime.AddMinutes(-3));
        var firstBroker = Broker(BrokerOrder("account-11"));
        var secondBroker = Broker(BrokerOrder("account-22"));
        var firstAccount = Context(11, firstBroker.Object);
        var secondAccount = Context(22, secondBroker.Object);
        _store.Setup(store => store.LoadPendingAsync(37, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, earlier, second]);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstAccount);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondAccount);
        _coordinator.Setup(coordinator => coordinator.ReconcileAsync(
                It.IsAny<TradeRecommendation>(),
                It.IsAny<AccountBrokerContext>(),
                It.IsAny<IReadOnlyCollection<BrokerOrder>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.AwaitingBroker));

        await CreateSut().RunAsync();

        firstBroker.Verify(broker => broker.GetOrderHistoryAsync(
            earlier.EntryRequestedAt!.Value.AddSeconds(-2),
            _now.UtcDateTime.AddSeconds(1),
            It.IsAny<CancellationToken>()), Times.Once);
        secondBroker.Verify(broker => broker.GetOrderHistoryAsync(
            second.EntryRequestedAt!.Value.AddSeconds(-2),
            _now.UtcDateTime.AddSeconds(1),
            It.IsAny<CancellationToken>()), Times.Once);
        _coordinator.Verify(coordinator => coordinator.ReconcileAsync(
            first,
            firstAccount,
            It.Is<IReadOnlyCollection<BrokerOrder>>(orders =>
                orders.Single().OrderId == "account-11"),
            It.IsAny<CancellationToken>()), Times.Once);
        _coordinator.Verify(coordinator => coordinator.ReconcileAsync(
            second,
            secondAccount,
            It.Is<IReadOnlyCollection<BrokerOrder>>(orders =>
                orders.Single().OrderId == "account-22"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OneAccountHistoryFailureDoesNotBlockAnotherAccount()
    {
        var failed = Recommendation(1, 11, _now.UtcDateTime.AddMinutes(-5));
        var healthy = Recommendation(2, 22, _now.UtcDateTime.AddMinutes(-4));
        var failedBroker = Broker();
        var healthyBroker = Broker(BrokerOrder("healthy-order"));
        var failedAccount = Context(11, failedBroker.Object);
        var healthyAccount = Context(22, healthyBroker.Object);
        _store.Setup(store => store.LoadPendingAsync(37, It.IsAny<CancellationToken>()))
            .ReturnsAsync([failed, healthy]);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedAccount);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthyAccount);
        failedBroker.Setup(broker => broker.GetOrderHistoryAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("account 11 unavailable"));
        _coordinator.Setup(coordinator => coordinator.ReconcileAsync(
                healthy,
                healthyAccount,
                It.IsAny<IReadOnlyCollection<BrokerOrder>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.AwaitingBroker));

        await CreateSut().RunAsync();

        _coordinator.Verify(coordinator => coordinator.ReconcileAsync(
            failed,
            It.IsAny<AccountBrokerContext>(),
            It.IsAny<IReadOnlyCollection<BrokerOrder>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _coordinator.Verify(coordinator => coordinator.ReconcileAsync(
            healthy,
            healthyAccount,
            It.Is<IReadOnlyCollection<BrokerOrder>>(orders =>
                orders.Single().OrderId == "healthy-order"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingDurableOwnershipNeverFallsBackToActiveAccount()
    {
        _store.Setup(store => store.LoadPendingAsync(37, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Recommendation(1, null, _now.UtcDateTime.AddMinutes(-5)),
                Recommendation(2, 11, requestedAt: null),
            ]);

        await CreateSut().RunAsync();

        _accounts.Verify(manager => manager.GetBrokerContextAsync(
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(manager => manager.GetBrokerContextForReconciliationAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _coordinator.VerifyNoOtherCalls();
    }

    private LiveEntryReconciliationCycle CreateSut() => new(
        _store.Object,
        _coordinator.Object,
        _accounts.Object,
        Options.Create(new TradingSettings { EntryReconciliationBatchSize = 37 }),
        new FixedTimeProvider(_now),
        NullLogger<LiveEntryReconciliationCycle>.Instance);

    private static TradeRecommendation Recommendation(
        long id,
        int? accountId,
        DateTime? requestedAt) => new()
    {
        Id = id,
        Symbol = $"TEST{id}",
        EntryAccountId = accountId,
        EntryRequestedAt = requestedAt,
    };

    private static Mock<IBrokerService> Broker(params BrokerOrder[] orders)
    {
        var broker = new Mock<IBrokerService>();
        broker.SetupGet(value => value.BrokerType).Returns(BrokerType.Alpaca);
        broker.Setup(value => value.GetOrderHistoryAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders.ToList());
        return broker;
    }

    private static AccountBrokerContext Context(int accountId, IBrokerService broker) => new(
        new ManagedTradingAccount
        {
            Id = accountId,
            BrokerType = BrokerType.Alpaca,
        },
        broker);

    private static BrokerOrder BrokerOrder(string orderId) => new()
    {
        OrderId = orderId,
        SubmittedAt = new DateTime(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

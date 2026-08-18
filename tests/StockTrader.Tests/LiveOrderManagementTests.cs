using FluentAssertions;
using Moq;
using System.Text.Json;
using StockTrader.Api.Contracts;
using StockTrader.Application.Accounts;
using StockTrader.Application.Execution;
using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public sealed class LiveOrderManagementTests
{
    private readonly Mock<IAccountManager> _accounts = new();
    private readonly Mock<IOpenPositionStore> _positions = new();
    private readonly Mock<ILivePositionExecutionCoordinator> _positionExecutions = new();
    private readonly Mock<ILiveEntryExecutionStore> _entryStore = new();
    private readonly Mock<ILiveEntryExecutionCoordinator> _entryExecutions = new();
    private readonly DateTimeOffset _now = new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClosePosition_UsesOwningAccountInsteadOfActiveAccount()
    {
        var position = Position(accountId: 42);
        var account = Context(42);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _accounts.Setup(manager => manager.GetBrokerContextForPositionExitAsync(
                42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionExecutions.Setup(coordinator => coordinator.SubmitFullExitAsync(
                position, "사용자 수동 청산", account.Broker, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LivePositionExecutionSubmission(
                LivePositionExecutionSubmissionStatus.Accepted,
                _now.UtcDateTime,
                new BrokerOrder { Status = BrokerOrderStatus.Accepted },
                BrokerOrderIdPersisted: true));

        var result = await CreateSut().ClosePositionAsync(" tqqq ");

        result.IsSuccess.Should().BeTrue();
        result.Accepted.Should().BeTrue();
        result.Status.Should().Be("Accepted");
        result.BrokerOrderIdPersisted.Should().BeTrue();
        _accounts.Verify(manager => manager.GetBrokerContextForPositionExitAsync(
            42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClosePosition_LegacyPositionUsesActiveAccountFallback()
    {
        var position = Position(accountId: 0);
        var account = Context(7);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _accounts.Setup(manager => manager.GetBrokerContextAsync(
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionExecutions.Setup(coordinator => coordinator.SubmitFullExitAsync(
                position, It.IsAny<string>(), account.Broker, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LivePositionExecutionSubmission(
                LivePositionExecutionSubmissionStatus.AlreadyPending, _now.UtcDateTime));

        var result = await CreateSut().ClosePositionAsync("TQQQ");

        result.Status.Should().Be("AlreadyPending");
        result.Accepted.Should().BeFalse();
    }

    [Fact]
    public async Task ClosePosition_AmbiguousSymbolFailsBeforeBrokerSelection()
    {
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Position(1), Position(2)]);

        var result = await CreateSut().ClosePositionAsync("TQQQ");

        result.Failure.Should().Be(LiveOrderManagementFailure.InvalidRequest);
        result.Error.Should().Contain("여러 계좌");
        _accounts.Verify(manager => manager.GetBrokerContextAsync(
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcilePosition_UsesOwningAccountEvenWhenInactive()
    {
        var position = Position(42);
        position.ExecutionRequestedAt = _now.UtcDateTime.AddMinutes(-1);
        var account = Context(42);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionExecutions.Setup(coordinator => coordinator.ReconcileAsync(
                position, account.Broker, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.Completed,
                new BrokerOrder
                {
                    Status = BrokerOrderStatus.Filled,
                    AverageFillPrice = 55m,
                    FilledQuantity = 10,
                },
                FilledQuantity: 10,
                IsFullExit: true));

        var result = await CreateSut().ReconcilePositionAsync("tqqq");

        result.Status.Should().Be("Completed");
        result.FillPrice.Should().Be(55m);
        result.FilledQuantity.Should().Be(10);
    }

    [Fact]
    public async Task ReconcilePosition_NotPendingDoesNotResolveBroker()
    {
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Position(42)]);

        var result = await CreateSut().ReconcilePositionAsync("TQQQ");

        result.Status.Should().Be("NotPending");
        _accounts.Verify(manager => manager.GetBrokerContextForReconciliationAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileEntry_UsesDurableEntryAccount()
    {
        var recommendation = new TradeRecommendation
        {
            Id = 81,
            Symbol = "AAPL",
            EntryRequestedAt = _now.UtcDateTime.AddMinutes(-1),
            EntryAccountId = 17,
        };
        var account = Context(17);
        _entryStore.Setup(store => store.LoadAsync(81, It.IsAny<CancellationToken>()))
            .ReturnsAsync(recommendation);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _entryExecutions.Setup(coordinator => coordinator.ReconcileAsync(
                recommendation, account, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.AwaitingBroker,
                new BrokerOrder { Status = BrokerOrderStatus.Accepted }));

        var result = await CreateSut().ReconcileEntryAsync(81);

        result.Status.Should().Be("AwaitingBroker");
        result.Message.Should().Contain("기다리고");
    }

    [Fact]
    public async Task ReconcileEntry_MissingDurableAccountFailsClosed()
    {
        _entryStore.Setup(store => store.LoadAsync(81, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeRecommendation
            {
                Id = 81,
                EntryRequestedAt = _now.UtcDateTime.AddMinutes(-1),
            });

        var result = await CreateSut().ReconcileEntryAsync(81);

        result.Failure.Should().Be(LiveOrderManagementFailure.Conflict);
        result.Error.Should().Contain("계좌 정보가 없어");
    }

    [Fact]
    public void ApiContracts_OmitUnavailableFieldsToPreserveExistingWireShape()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var success = JsonSerializer.SerializeToElement(
            new LiveOrderResponse("NotPending", "대기 주문 없음", null, null, null, 0, null),
            options);
        var failure = JsonSerializer.SerializeToElement(
            new LiveOrderErrorResponse("실패"), options);

        success.TryGetProperty("requestedAt", out _).Should().BeFalse();
        success.TryGetProperty("brokerStatus", out _).Should().BeFalse();
        success.GetProperty("filledQuantity").GetInt32().Should().Be(0);
        failure.TryGetProperty("status", out _).Should().BeFalse();
    }

    private LiveOrderManagement CreateSut() => new(
        _accounts.Object,
        _positions.Object,
        _positionExecutions.Object,
        _entryStore.Object,
        _entryExecutions.Object,
        new FixedTimeProvider(_now));

    private static Position Position(int accountId) => new()
    {
        Id = accountId + 100,
        AccountId = accountId,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
        EntryPrice = 50m,
    };

    private static AccountBrokerContext Context(int accountId)
    {
        var broker = new Mock<IBrokerService>();
        broker.SetupGet(service => service.BrokerType).Returns(BrokerType.Alpaca);
        return new AccountBrokerContext(
            new ManagedTradingAccount { Id = accountId, BrokerType = BrokerType.Alpaca },
            broker.Object);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

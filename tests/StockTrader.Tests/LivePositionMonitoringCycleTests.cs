using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Accounts;
using StockTrader.Application.Execution;
using StockTrader.Application.Settings;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public sealed class LivePositionMonitoringCycleTests
{
    private readonly Mock<IOpenPositionStore> _positions = new();
    private readonly Mock<IOhlcvRepository> _bars = new();
    private readonly Mock<ILiveParameterService> _liveParameters = new();
    private readonly Mock<ICompiledStrategyRepository> _strategies = new();
    private readonly Mock<IAccountManager> _accounts = new();
    private readonly Mock<ILivePositionExecutionCoordinator> _executions = new();
    private readonly Mock<ILivePositionExecutionEvaluator> _evaluator = new();
    private readonly Mock<INotificationService> _notifications = new();

    [Fact]
    public async Task PendingOrdersReconcileThroughEachDurableOwningAccount()
    {
        var first = Position(11, pending: true);
        var second = Position(22, pending: true);
        var firstAccount = Context(11, 111m, 11_000m);
        var secondAccount = Context(22, 222m, 22_000m);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstAccount);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondAccount);
        _executions.Setup(coordinator => coordinator.ReconcileAsync(
                It.IsAny<Position>(),
                It.IsAny<IBrokerService>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.AwaitingBroker));

        await CreateSut().RunAsync();

        _executions.Verify(coordinator => coordinator.ReconcileAsync(
            first, firstAccount.Broker, null, It.IsAny<CancellationToken>()), Times.Once);
        _executions.Verify(coordinator => coordinator.ReconcileAsync(
            second, secondAccount.Broker, null, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(manager => manager.GetBrokerContextAsync(
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _liveParameters.Verify(service => service.GetAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PositionPendingAtCycleStartIsNotReevaluatedAfterReconciliationCompletes()
    {
        var position = Position(11, pending: true);
        var account = Context(11, 111m, 11_000m);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _accounts.Setup(manager => manager.GetBrokerContextForReconciliationAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _executions.Setup(coordinator => coordinator.ReconcileAsync(
                position, account.Broker, null, It.IsAny<CancellationToken>()))
            .Callback(() => position.ExecutionRequestedAt = null)
            .ReturnsAsync(new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.Completed));

        await CreateSut().RunAsync();

        _evaluator.Verify(service => service.EvaluateAsync(
            It.IsAny<Position>(),
            It.IsAny<CompiledStrategy?>(),
            It.IsAny<IOhlcvRepository>(),
            It.IsAny<PatternParameterOverrides?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<decimal>(),
            It.IsAny<int>()), Times.Never);
        _liveParameters.Verify(service => service.GetAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluationUsesThePriceAndEquityFromEachOwningAccount()
    {
        var first = Position(11);
        var second = Position(22);
        var firstAccount = Context(11, 111m, 11_000m);
        var secondAccount = Context(22, 222m, 22_000m);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);
        _liveParameters.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveParameterSnapshot([], null));
        _strategies.Setup(repository => repository.GetByNamesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CompiledStrategy>());
        _accounts.Setup(manager => manager.GetBrokerContextForPositionExitAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstAccount);
        _accounts.Setup(manager => manager.GetBrokerContextForPositionExitAsync(
                22, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondAccount);
        _evaluator.Setup(service => service.EvaluateAsync(
                first, null, _bars.Object, null, It.IsAny<CancellationToken>(), 11_000m, 7))
            .ReturnsAsync(new LivePositionExecutionDecision(null));
        _evaluator.Setup(service => service.EvaluateAsync(
                second, null, _bars.Object, null, It.IsAny<CancellationToken>(), 22_000m, 7))
            .ReturnsAsync(new LivePositionExecutionDecision(null));

        await CreateSut().RunAsync();

        first.CurrentPrice.Should().Be(111m);
        second.CurrentPrice.Should().Be(222m);
        _evaluator.VerifyAll();
        _accounts.Verify(manager => manager.GetActiveBrokerServiceAsync(
            It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(manager => manager.GetBrokerContextAsync(
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyAccountlessPositionUsesTheExplicitActiveAccountFallback()
    {
        var legacy = Position(0);
        var active = Context(7, 77m, 7_000m);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([legacy]);
        _liveParameters.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveParameterSnapshot([], null));
        _strategies.Setup(repository => repository.GetByNamesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CompiledStrategy>());
        _accounts.Setup(manager => manager.GetBrokerContextAsync(
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(active);
        _evaluator.Setup(service => service.EvaluateAsync(
                legacy, null, _bars.Object, null, It.IsAny<CancellationToken>(), 7_000m, 7))
            .ReturnsAsync(new LivePositionExecutionDecision(null));

        await CreateSut().RunAsync();

        legacy.CurrentPrice.Should().Be(77m);
        _accounts.Verify(manager => manager.GetBrokerContextForPositionExitAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisabledOwningAccountCanBeMonitoredButCannotScaleIn()
    {
        var position = Position(11);
        var disabled = Context(11, 111m, 11_000m, isEnabled: false);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _liveParameters.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveParameterSnapshot([], null));
        _strategies.Setup(repository => repository.GetByNamesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CompiledStrategy>());
        _accounts.Setup(manager => manager.GetBrokerContextForPositionExitAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(disabled);
        _evaluator.Setup(service => service.EvaluateAsync(
                position, null, _bars.Object, null, It.IsAny<CancellationToken>(), 11_000m, 7))
            .ReturnsAsync(new LivePositionExecutionDecision(
                new LiveLongPositionExecutionIntent(
                    5, "add", PositionExecutionKind.ScaleIn, ScalingRuleIndex: 0)));

        await CreateSut().RunAsync();

        _executions.Verify(coordinator => coordinator.SubmitAsync(
            It.IsAny<Position>(),
            It.IsAny<LivePositionExecutionRequest>(),
            It.IsAny<IBrokerService>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisabledOwningAccountStillAllowsRiskReducingExit()
    {
        var position = Position(11);
        var disabled = Context(11, 40m, 11_000m, isEnabled: false);
        _positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([position]);
        _liveParameters.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveParameterSnapshot([], null));
        _strategies.Setup(repository => repository.GetByNamesAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CompiledStrategy>());
        _accounts.Setup(manager => manager.GetBrokerContextForPositionExitAsync(
                11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(disabled);
        _evaluator.Setup(service => service.EvaluateAsync(
                position, null, _bars.Object, null, It.IsAny<CancellationToken>(), 11_000m, 7))
            .ReturnsAsync(new LivePositionExecutionDecision(
                new LiveLongPositionExecutionIntent(
                    10, "stop", PositionExecutionKind.FullExit)));
        _executions.Setup(coordinator => coordinator.SubmitAsync(
                position,
                It.Is<LivePositionExecutionRequest>(request =>
                    request.Kind == PositionExecutionKind.FullExit),
                disabled.Broker,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LivePositionExecutionSubmission(
                LivePositionExecutionSubmissionStatus.Failed));

        await CreateSut().RunAsync();

        _executions.Verify(coordinator => coordinator.SubmitAsync(
            position,
            It.Is<LivePositionExecutionRequest>(request =>
                request.Kind == PositionExecutionKind.FullExit),
            disabled.Broker,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private LivePositionMonitoringCycle CreateSut() => new(
        _positions.Object,
        _bars.Object,
        _liveParameters.Object,
        _strategies.Object,
        _accounts.Object,
        _executions.Object,
        _evaluator.Object,
        _notifications.Object,
        Options.Create(new TradingSettings
        {
            MaxTotalPositions = 7,
            PositionOrderResolutionMaxAttempts = 1,
            PositionOrderResolutionDelayMilliseconds = 1,
        }),
        TimeProvider.System,
        NullLogger<LivePositionMonitoringCycle>.Instance);

    private static Position Position(int accountId, bool pending = false) => new()
    {
        Id = accountId + 100,
        AccountId = accountId,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
        EntryPrice = 50m,
        ExecutionRequestedAt = pending
            ? new DateTime(2026, 8, 19, 1, 0, 0, DateTimeKind.Utc)
            : null,
    };

    private static AccountBrokerContext Context(
        int accountId,
        decimal currentPrice,
        decimal equity,
        bool isEnabled = true)
    {
        var broker = new Mock<IBrokerService>();
        broker.SetupGet(service => service.BrokerType).Returns(BrokerType.Alpaca);
        broker.Setup(service => service.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrokerAccount { TotalEquity = equity });
        broker.Setup(service => service.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BrokerPositionSnapshot("TQQQ", 10, 50m, currentPrice)
            ]);
        return new AccountBrokerContext(
            new ManagedTradingAccount
            {
                Id = accountId,
                BrokerType = BrokerType.Alpaca,
                IsEnabled = isEnabled,
            },
            broker.Object);
    }
}

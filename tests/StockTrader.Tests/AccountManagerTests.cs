using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.Accounts;
using StockTrader.Models;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;

namespace StockTrader.Tests;

public sealed class AccountManagerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 18, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BrokerInstancesAreCachedAndAccountUpdateInvalidatesCache()
    {
        var account = Account();
        var store = new Mock<ITradingAccountStore>();
        var factory = new Mock<IAccountBrokerServiceFactory>();
        var firstBroker = new Mock<IBrokerService>().Object;
        var secondBroker = new Mock<IBrokerService>().Object;
        store.Setup(item => item.LoadByIdAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        store.Setup(item => item.UpdateAsync(
                It.IsAny<ManagedTradingAccount>(), Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedTradingAccount value, DateTime _, CancellationToken _) => value);
        factory.SetupSequence(item => item.Create(It.IsAny<ManagedTradingAccount>()))
            .Returns(firstBroker)
            .Returns(secondBroker);
        var manager = Manager(store, factory);

        (await manager.GetBrokerServiceForAccountAsync(account.Id))
            .Should().BeSameAs(firstBroker);
        (await manager.GetBrokerServiceForAccountAsync(account.Id))
            .Should().BeSameAs(firstBroker);
        await manager.UpdateAccountAsync(account with { AccountName = "Changed" });
        (await manager.GetBrokerServiceForAccountAsync(account.Id))
            .Should().BeSameAs(secondBroker);

        factory.Verify(item => item.Create(It.IsAny<ManagedTradingAccount>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SuccessfulConnectionUsesOneClockValueAndAwaitsPersistence()
    {
        var account = Account();
        var store = new Mock<ITradingAccountStore>();
        var factory = new Mock<IAccountBrokerServiceFactory>();
        var broker = new Mock<IBrokerService>();
        store.Setup(item => item.LoadByIdAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        broker.Setup(item => item.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrokerAccount
            {
                TotalEquity = 12_000m,
                Cash = 4_000m,
                BuyingPower = 8_000m
            });
        broker.Setup(item => item.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Position { Symbol = "TQQQ" }]);
        factory.Setup(item => item.Create(account)).Returns(broker.Object);
        var manager = Manager(store, factory);

        var result = await manager.GetConnectionStatusAsync(account.Id);

        result.IsConnected.Should().BeTrue();
        result.CheckedAt.Should().Be(Now);
        result.TotalEquity.Should().Be(12_000m);
        result.OpenPositionCount.Should().Be(1);
        store.Verify(item => item.TouchLastConnectedAsync(
            account.Id, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CachedActiveAccountIsRevalidatedBeforeUse()
    {
        var first = Account() with { IsActive = true };
        var fallback = Account() with { Id = 2, AccountName = "Fallback", IsActive = true };
        var store = new Mock<ITradingAccountStore>();
        var factory = new Mock<IAccountBrokerServiceFactory>();
        store.SetupSequence(item => item.LoadActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(fallback);
        store.Setup(item => item.LoadByIdAsync(first.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(first with { IsActive = false });
        var manager = Manager(store, factory);

        (await manager.GetActiveAccountAsync())!.Id.Should().Be(first.Id);
        (await manager.GetActiveAccountAsync())!.Id.Should().Be(fallback.Id);
    }

    private static AccountManager Manager(
        Mock<ITradingAccountStore> store,
        Mock<IAccountBrokerServiceFactory> factory) => new(
            store.Object,
            factory.Object,
            new FixedTimeProvider(new DateTimeOffset(Now)),
            NullLogger<AccountManager>.Instance);

    private static ManagedTradingAccount Account() => new()
    {
        Id = 1,
        AccountName = "Paper",
        BrokerType = BrokerType.Alpaca,
        ApiKey = "key",
        ApiSecret = "secret",
        Environment = "Paper",
        IsActive = true,
        IsEnabled = true
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

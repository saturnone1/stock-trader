using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public class OrderServiceTests
{
    private readonly Mock<IAccountManager> _accountManagerMock;
    private readonly Mock<ITradeRepository> _tradeRepoMock;
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IBrokerService> _brokerServiceMock;

    public OrderServiceTests()
    {
        _accountManagerMock = new Mock<IAccountManager>();
        _tradeRepoMock = new Mock<ITradeRepository>();
        _settingsRepoMock = new Mock<ISettingsRepository>();
        _notificationServiceMock = new Mock<INotificationService>();
        _brokerServiceMock = new Mock<IBrokerService>();
    }

    // ── SUT 팩토리 ──────────────────────────────────────────────────────────

    private OrderService CreateSut() => new OrderService(
        _accountManagerMock.Object,
        _tradeRepoMock.Object,
        _settingsRepoMock.Object,
        _notificationServiceMock.Object,
        NullLogger<OrderService>.Instance);

    // ── 테스트 데이터 헬퍼 ─────────────────────────────────────────────────

    private static TradeRecommendation CreateRecommendation(
        string symbol = "AAPL",
        int shareQuantity = 10,
        decimal entryPrice = 150m,
        decimal stopLossPrice = 145m,
        decimal targetPrice = 160m)
    {
        return new TradeRecommendation
        {
            Symbol = symbol,
            PatternType = PatternType.GapUpPullback,
            GeneratedAt = DateTime.UtcNow,
            EntryPrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TargetPrice = targetPrice,
            ShareQuantity = shareQuantity,
            PositionSize = entryPrice * shareQuantity,
            WasExecuted = false
        };
    }

    private void SetupSettingsWithMode(OrderMode mode)
    {
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { OrderMode = mode });
    }

    // ── PlaceOrder: AlertOnly 모드 ─────────────────────────────────────────

    /// <summary>
    /// AlertOnly 모드에서는 추천 내역을 DB에 저장하고 true를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AlertOnly_SavesRecommendationAndReturnsTrue()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AlertOnly);
        var rec = CreateRecommendation();
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec);

        // Assert
        result.Should().BeTrue();
        _tradeRepoMock.Verify(
            r => r.AddRecommendationAsync(rec, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AlertOnly 모드에서는 INotificationService.Notify를 반드시 호출해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AlertOnly_NotifiesUser()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AlertOnly);
        var rec = CreateRecommendation();
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        _notificationServiceMock.Verify(
            n => n.Notify(rec),
            Times.Once);
    }

    /// <summary>
    /// AlertOnly 모드에서는 브로커 서비스를 호출하지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AlertOnly_DoesNotCallBroker()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AlertOnly);
        var rec = CreateRecommendation();
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        _accountManagerMock.Verify(
            m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _accountManagerMock.Verify(
            m => m.GetBrokerServiceForAccountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── PlaceOrder: AutoOrder 모드 — 수량 검증 ─────────────────────────────

    /// <summary>
    /// AutoOrder 모드에서 ShareQuantity가 0이면 false를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_ZeroQuantity_ReturnsFalse()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        var rec = CreateRecommendation(shareQuantity: 0);
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// AutoOrder 모드에서 ShareQuantity가 0이어도 추천 내역은 저장되어야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_ZeroQuantity_StillSavesRecommendation()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        var rec = CreateRecommendation(shareQuantity: 0);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        _tradeRepoMock.Verify(
            r => r.AddRecommendationAsync(rec, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── PlaceOrder: AutoOrder 모드 — 브로커 없음 ───────────────────────────

    /// <summary>
    /// AutoOrder 모드에서 활성 계좌의 브로커 서비스가 없으면 false를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_NoBroker_ReturnsFalse()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBrokerService?)null);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// AutoOrder + 브로커 없음 상황에서도 포지션은 저장되지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_NoBroker_DoesNotSavePosition()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBrokerService?)null);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        _tradeRepoMock.Verify(
            r => r.SavePositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── PlaceOrder: AutoOrder 모드 — 브로커 성공 ───────────────────────────

    /// <summary>
    /// AutoOrder 모드에서 브로커 주문이 성공하면 Position을 저장하고 true를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_BrokerSuccess_SavesPositionAndReturnsTrue()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rec = CreateRecommendation(shareQuantity: 10, entryPrice: 150m, stopLossPrice: 145m, targetPrice: 165m);
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec);

        // Assert
        result.Should().BeTrue();
        _tradeRepoMock.Verify(
            r => r.SavePositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// AutoOrder 모드에서 브로커 주문이 성공하면 recommendation.WasExecuted가 true로 설정되어야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_BrokerSuccess_SetsWasExecutedTrue()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        rec.WasExecuted.Should().BeTrue();
    }

    /// <summary>
    /// AutoOrder 모드에서 브로커 주문이 성공하면 올바른 데이터로 Position이 저장되어야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_BrokerSuccess_SavesPositionWithCorrectData()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rec = CreateRecommendation(
            symbol: "TSLA",
            shareQuantity: 5,
            entryPrice: 200m,
            stopLossPrice: 190m,
            targetPrice: 220m);
        var sut = CreateSut();

        Position? savedPosition = null;
        _tradeRepoMock
            .Setup(r => r.SavePositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<Position, CancellationToken>((pos, _) => savedPosition = pos)
            .Returns(Task.CompletedTask);

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.Symbol.Should().Be("TSLA");
        savedPosition.Quantity.Should().Be(5);
        savedPosition.EntryPrice.Should().Be(200m);
        savedPosition.CurrentPrice.Should().Be(200m);
        savedPosition.StopLossPrice.Should().Be(190m);
        savedPosition.TargetPrice.Should().Be(220m);
        savedPosition.PatternType.Should().Be(PatternType.GapUpPullback);
    }

    // ── PlaceOrder: AutoOrder 모드 — 브로커 실패 ───────────────────────────

    /// <summary>
    /// AutoOrder 모드에서 브로커 주문이 실패하면 false를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_BrokerFail_ReturnsFalse()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// AutoOrder 모드에서 브로커 주문이 실패하면 Position을 저장하지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_BrokerFail_DoesNotSavePosition()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec);

        // Assert
        _tradeRepoMock.Verify(
            r => r.SavePositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── PlaceOrder: 특정 계좌 ID 지정 ─────────────────────────────────────

    /// <summary>
    /// accountId를 명시하면 GetBrokerServiceForAccount(accountId)를 호출해야 한다.
    /// GetActiveBrokerService()를 호출해서는 안 된다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_SpecificAccount_UsesCorrectBroker()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        const int targetAccountId = 42;
        _accountManagerMock
            .Setup(m => m.GetBrokerServiceForAccountAsync(targetAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        var result = await sut.PlaceOrderAsync(rec, accountId: targetAccountId);

        // Assert
        result.Should().BeTrue();
        _accountManagerMock.Verify(m => m.GetBrokerServiceForAccountAsync(targetAccountId, It.IsAny<CancellationToken>()), Times.Once);
        _accountManagerMock.Verify(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// accountId가 null이면 GetActiveBrokerService()를 호출해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AutoOrder_NullAccountId_UsesActiveBroker()
    {
        // Arrange
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.PlaceOrderAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var rec = CreateRecommendation(shareQuantity: 10);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(rec, accountId: null);

        // Assert
        _accountManagerMock.Verify(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()), Times.Once);
        _accountManagerMock.Verify(
            m => m.GetBrokerServiceForAccountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── PlaceOrder: 추천 내역은 항상 저장 ─────────────────────────────────

    /// <summary>
    /// OrderMode에 관계없이 PlaceOrderAsync 호출 시 추천 내역을 항상 저장해야 한다.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_AlwaysSavesRecommendation_RegardlessOfMode()
    {
        // Arrange — AlertOnly
        SetupSettingsWithMode(OrderMode.AlertOnly);
        var recAlert = CreateRecommendation(symbol: "AAPL");
        var sut = CreateSut();
        await sut.PlaceOrderAsync(recAlert);

        // AlertOnly 저장 확인
        _tradeRepoMock.Verify(
            r => r.AddRecommendationAsync(recAlert, It.IsAny<CancellationToken>()),
            Times.Once);

        // Arrange — AutoOrder (브로커 연결 없음)
        SetupSettingsWithMode(OrderMode.AutoOrder);
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBrokerService?)null);

        var recAuto = CreateRecommendation(symbol: "TSLA", shareQuantity: 10);
        await sut.PlaceOrderAsync(recAuto);

        // AutoOrder 저장 확인
        _tradeRepoMock.Verify(
            r => r.AddRecommendationAsync(recAuto, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── CancelOrderAsync ──────────────────────────────────────────────────

    /// <summary>
    /// 활성 브로커 서비스가 없으면 취소 요청 시 false를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task CancelOrder_NoBroker_ReturnsFalse()
    {
        // Arrange
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBrokerService?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.CancelOrderAsync("order-001");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// 브로커가 취소를 성공하면 true를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task CancelOrder_BrokerSuccess_ReturnsTrue()
    {
        // Arrange
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.CancelOrderAsync("order-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateSut();

        // Act
        var result = await sut.CancelOrderAsync("order-123");

        // Assert
        result.Should().BeTrue();
        _brokerServiceMock.Verify(b => b.CancelOrderAsync("order-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 브로커 취소가 실패하면 false를 반환해야 한다.
    /// </summary>
    [Fact]
    public async Task CancelOrder_BrokerFail_ReturnsFalse()
    {
        // Arrange
        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.CancelOrderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateSut();

        // Act
        var result = await sut.CancelOrderAsync("order-456");

        // Assert
        result.Should().BeFalse();
    }

    // ── GetOpenPositionsAsync ──────────────────────────────────────────────

    /// <summary>
    /// 브로커가 포지션을 반환하면 해당 포지션 목록을 우선적으로 반환해야 한다.
    /// DB 폴백이 발생해서는 안 된다.
    /// </summary>
    [Fact]
    public async Task GetOpenPositions_BrokerHasPositions_ReturnsBrokerPositions()
    {
        // Arrange
        var brokerPositions = new List<Position>
        {
            new() { Symbol = "AAPL", Quantity = 10, EntryPrice = 150m, CurrentPrice = 155m },
            new() { Symbol = "MSFT", Quantity = 5, EntryPrice = 300m, CurrentPrice = 310m }
        };

        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(brokerPositions);

        var sut = CreateSut();

        // Act
        var result = await sut.GetOpenPositionsAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(brokerPositions);

        // DB 폴백 미호출 확인
        _tradeRepoMock.Verify(
            r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// 브로커 포지션이 비어 있으면 DB 폴백을 사용해야 한다.
    /// </summary>
    [Fact]
    public async Task GetOpenPositions_BrokerEmpty_FallsBackToDb()
    {
        // Arrange
        var dbPositions = new List<Position>
        {
            new() { Symbol = "NVDA", Quantity = 3, EntryPrice = 500m, CurrentPrice = 510m }
        };

        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_brokerServiceMock.Object);
        _brokerServiceMock
            .Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>()); // 브로커 빈 목록 반환

        _tradeRepoMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbPositions);

        var sut = CreateSut();

        // Act
        var result = await sut.GetOpenPositionsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("NVDA");

        // DB 폴백 호출 확인
        _tradeRepoMock.Verify(
            r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// 브로커 서비스가 없으면 DB에서 포지션을 가져와야 한다.
    /// </summary>
    [Fact]
    public async Task GetOpenPositions_NoBroker_FallsBackToDb()
    {
        // Arrange
        var dbPositions = new List<Position>
        {
            new() { Symbol = "GOOG", Quantity = 2, EntryPrice = 140m, CurrentPrice = 145m }
        };

        _accountManagerMock
            .Setup(m => m.GetActiveBrokerServiceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBrokerService?)null);

        _tradeRepoMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dbPositions);

        var sut = CreateSut();

        // Act
        var result = await sut.GetOpenPositionsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("GOOG");
    }
}

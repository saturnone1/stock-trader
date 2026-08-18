using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Accounts;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Risk;

namespace StockTrader.Tests;

public class MultiAccountRiskServiceTests
{
    private readonly Mock<IOpenPositionStore> _positionStoreMock;
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly Mock<IAccountManager> _accountManagerMock;
    private readonly TradingSettings _defaultSettings;

    public MultiAccountRiskServiceTests()
    {
        _positionStoreMock = new Mock<IOpenPositionStore>();
        _settingsRepoMock = new Mock<ISettingsRepository>();
        _accountManagerMock = new Mock<IAccountManager>();

        _defaultSettings = new TradingSettings
        {
            DefaultAccountSize = 100_000m,
            RiskPerTradePercent = 0.01m,
            DailyLossLimitPercent = 0.03m,
            MaxPositionsPerSector = 2,
            MaxTotalPositions = 10
        };
    }

    private MultiAccountRiskService CreateSut(TradingSettings? settings = null)
    {
        var opts = Options.Create(settings ?? _defaultSettings);

        // IServiceScopeFactory mock: CreateScope → ServiceProvider → GetRequiredService
        var scopeFactory = CreateMockScopeFactory();

        var cache = new MemoryCache(new MemoryCacheOptions());

        return new MultiAccountRiskService(
            scopeFactory,
            opts,
            _accountManagerMock.Object,
            cache,
            NullLogger<MultiAccountRiskService>.Instance);
    }

    private IServiceScopeFactory CreateMockScopeFactory()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IOpenPositionStore)))
            .Returns(_positionStoreMock.Object);
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ISettingsRepository)))
            .Returns(_settingsRepoMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return scopeFactoryMock.Object;
    }

    private static ManagedTradingAccount MakeAccount(int id = 1, bool isActive = true) =>
        new ManagedTradingAccount
        {
            Id = id,
            AccountName = $"Test Account #{id}",
            BrokerType = BrokerType.Alpaca,
            IsActive = isActive,
            IsEnabled = true
        };

    // ────────────────────────────────────────────────────────────
    // GetCurrentRiskStateAsync Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentRiskState_NoActiveAccount_ReturnsFallback()
    {
        // Arrange: 활성 계좌가 없는 상황
        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedTradingAccount?)null);

        var sut = CreateSut();

        // Act
        var state = await sut.GetCurrentRiskStateAsync();

        // Assert: fallback 상태는 안전 기본값(거래 중단 없음, PnL 0)
        state.Should().NotBeNull();
        state.IsTradingHalted.Should().BeFalse();
        state.DailyPnL.Should().Be(0m);
        state.OpenPositionCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentRiskState_WithActiveAccount_ReturnsAccountState()
    {
        // Arrange: 계좌 1이 활성, UpdateDailyPnLAsync로 상태를 먼저 주입
        var account = MakeAccount(id: 1);
        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                    EntryPrice = 100m, CurrentPrice = 102m, Quantity = 50 }
            // UnrealizedPnL = (102-100)*50 = 100
        };
        var userSettings = new UserSettings { AccountSize = 100_000m };

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _accountManagerMock
            .Setup(m => m.GetAllAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagedTradingAccount> { account });
        _accountManagerMock
            .Setup(m => m.GetBrokerServiceForAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTrader.Services.Broker.IBrokerService?)null); // 브로커 없으면 기본값 사용
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var sut = CreateSut();
        await sut.UpdateDailyPnLAsync();

        // Act
        var state = await sut.GetCurrentRiskStateAsync();

        // Assert: 계좌 1의 상태가 반환되어야 함 (PnL = 100, 포지션 1개)
        state.OpenPositionCount.Should().Be(1);
        state.DailyPnL.Should().Be(100m);
        state.IsTradingHalted.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // GetPortfolioRiskState / GetAccountRiskState Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void GetPortfolioRiskState_InitialState_ReturnsDefaultValues()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var portfolioState = sut.GetPortfolioRiskState();

        // Assert: 초기 포트폴리오 상태는 안전 기본값
        portfolioState.Should().NotBeNull();
        portfolioState.IsTradingHalted.Should().BeFalse();
        portfolioState.DailyPnL.Should().Be(0m);
    }

    [Fact]
    public void GetAccountRiskState_UnknownAccountId_ReturnsNewDefault()
    {
        // Arrange: 아무 상태도 주입하지 않은 상태에서 존재하지 않는 계좌 ID 조회
        var sut = CreateSut();

        // Act
        var state = sut.GetAccountRiskState(accountId: 999);

        // Assert: 새 RiskState 기본값 반환 (null 아님)
        state.Should().NotBeNull();
        state.IsTradingHalted.Should().BeFalse();
        state.DailyPnL.Should().Be(0m);
    }

    // ────────────────────────────────────────────────────────────
    // CanOpenPositionAsync Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanOpenPosition_NoActiveAccount_ReturnsFalse()
    {
        // Arrange: 활성 계좌 없음
        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedTradingAccount?)null);

        var sut = CreateSut();

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "Tech");

        // Assert
        allowed.Should().BeFalse();
        reason.Should().Contain("활성 계좌");
    }

    [Fact]
    public async Task CanOpenPosition_TradingHalted_ReturnsFalse()
    {
        // Arrange: 계좌 1에 TradingHalted 상태 주입
        var account = MakeAccount(id: 1);
        var lossPositions = new List<Position>
        {
            // -5% 손실 → 3% 한도 초과 → IsTradingHalted = true
            new() { Symbol = "AAPL", Sector = "Tech",
                    EntryPrice = 100m, CurrentPrice = 50m, Quantity = 100 }
        };
        var userSettings = new UserSettings { AccountSize = 100_000m };

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _accountManagerMock
            .Setup(m => m.GetAllAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagedTradingAccount> { account });
        _accountManagerMock
            .Setup(m => m.GetBrokerServiceForAccountAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTrader.Services.Broker.IBrokerService?)null);
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lossPositions);

        var sut = CreateSut();
        // UpdateDailyPnLAsync 호출로 IsTradingHalted 상태 설정
        await sut.UpdateDailyPnLAsync();

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        // Assert
        allowed.Should().BeFalse();
        reason.Should().Contain("Trading halted");
    }

    [Fact]
    public async Task CanOpenPosition_MaxPositionsReached_ReturnsFalse()
    {
        // Arrange: 최대 포지션 수(2) 도달
        var settings = new TradingSettings
        {
            MaxTotalPositions = 2,
            MaxPositionsPerSector = 5,
            DailyLossLimitPercent = 0.03m
        };
        var account = MakeAccount(id: 1);

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Symbol = "AAPL", Sector = "Tech" },
                new() { Symbol = "MSFT", Sector = "Tech" }
            });

        var sut = CreateSut(settings);

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        // Assert
        allowed.Should().BeFalse();
        reason.Should().Contain("Max total positions");
        reason.Should().Contain("2");
    }

    [Fact]
    public async Task CanOpenPosition_DuplicateSymbol_ReturnsFalse()
    {
        // Arrange: AAPL이 이미 오픈 포지션에 존재
        var account = MakeAccount(id: 1);

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Symbol = "AAPL", Sector = "Tech" }
            });

        var sut = CreateSut();

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "Tech");

        // Assert
        allowed.Should().BeFalse();
        reason.Should().Contain("Already have open position");
        reason.Should().Contain("AAPL");
    }

    [Fact]
    public async Task CanOpenPosition_SectorLimitReached_ReturnsFalse()
    {
        // Arrange: Tech 섹터에 이미 2개(MaxPositionsPerSector) 도달
        var settings = new TradingSettings
        {
            MaxTotalPositions = 10,
            MaxPositionsPerSector = 2,
            DailyLossLimitPercent = 0.03m
        };
        var account = MakeAccount(id: 1);

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Symbol = "AAPL", Sector = "Tech" },
                new() { Symbol = "MSFT", Sector = "Tech" }
            });

        var sut = CreateSut(settings);

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        // Assert
        allowed.Should().BeFalse();
        reason.Should().Contain("Max positions per sector");
        reason.Should().Contain("Tech");
    }

    [Fact]
    public async Task CanOpenPosition_AllClear_ReturnsTrue()
    {
        // Arrange: 제한 조건 없음 — 포지션 비어있고, 활성 계좌 존재
        var account = MakeAccount(id: 1);

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        var sut = CreateSut();

        // Act
        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "Tech");

        // Assert
        allowed.Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public async Task CanOpenPosition_EmptySector_SkipsSectorCheck()
    {
        // Arrange: sector가 빈 문자열이면 섹터 체크 건너뜀
        //          MaxPositionsPerSector=1로 설정해도 통과해야 함
        var settings = new TradingSettings
        {
            MaxTotalPositions = 10,
            MaxPositionsPerSector = 1,
            DailyLossLimitPercent = 0.03m
        };
        var account = MakeAccount(id: 1);

        _accountManagerMock
            .Setup(m => m.GetActiveAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        var sut = CreateSut(settings);

        // Act
        var (allowed, _) = await sut.CanOpenPositionAsync("AAPL", "");

        // Assert
        allowed.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    // CalculatePositionSize Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculatePositionSize_ValidInputs_ReturnsCorrectSize()
    {
        // accountSize=100000, riskPercent=0.01, entry=100, stopLoss=95
        // stopLossPercent = |100 - 95| / 100 = 0.05
        // positionSize = 100000 * 0.01 / 0.05 = 20000
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 95m);

        result.Should().Be(20_000m);
    }

    [Fact]
    public void CalculatePositionSize_ZeroStopLoss_ReturnsZero()
    {
        // stopLossPrice=0 이면 즉시 0 반환
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 0m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_ZeroEntryPrice_ReturnsZero()
    {
        // entryPrice=0 이면 즉시 0 반환 (division-by-zero 방지)
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 0m,
            stopLossPrice: 95m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_StopLossEqualsEntry_ReturnsZero()
    {
        // stopLossPercent = 0 → positionSize = 0 (division by zero 방지)
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 100m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_StopLossAboveEntry_CalculatesAbsoluteDifference()
    {
        // 숏 포지션 시나리오: entry=100, stopLoss=105
        // stopLossPercent = |100 - 105| / 100 = 0.05
        // positionSize = 100000 * 0.01 / 0.05 = 20000
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 105m);

        result.Should().Be(20_000m);
    }

    // ────────────────────────────────────────────────────────────
    // UpdateDailyPnLAsync — 포트폴리오 집계 검증
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDailyPnLAsync_SingleAccount_SetsPortfolioStateFromPositions()
    {
        // Arrange: 계좌 1개, 포지션 PnL +100 + (-50) = +50
        // 브로커 없음 → positionPnL을 effectivePnL로 사용
        // 포트폴리오 DailyPnL = 해당 계좌의 effectivePnL = 50
        var account1 = MakeAccount(id: 1);
        var userSettings = new UserSettings { AccountSize = 100_000m };

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                    EntryPrice = 100m, CurrentPrice = 101m, Quantity = 100 },
            // UnrealizedPnL = +100
            new() { Symbol = "TSLA", Sector = "Auto",
                    EntryPrice = 200m, CurrentPrice = 199.5m, Quantity = 100 }
            // UnrealizedPnL = -50
            // 합계 positionPnL = 50
        };

        _accountManagerMock
            .Setup(m => m.GetAllAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagedTradingAccount> { account1 });
        _accountManagerMock
            .Setup(m => m.GetBrokerServiceForAccountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTrader.Services.Broker.IBrokerService?)null);
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var sut = CreateSut();

        // Act
        await sut.UpdateDailyPnLAsync();

        // Assert: 단일 계좌 → 포트폴리오 PnL = positionPnL = 50
        var portfolio = sut.GetPortfolioRiskState();
        portfolio.DailyPnL.Should().Be(50m);
        portfolio.OpenPositionCount.Should().Be(2);
        portfolio.IsTradingHalted.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDailyPnLAsync_MultipleAccounts_PortfolioPnLIsSumOfAllAccountPnL()
    {
        // Arrange: 계좌 2개, 브로커 없어서 둘 다 같은 positionPnL(50)을 적용
        // 현재 구현: 각 계좌별로 전체 포지션 PnL을 독립적으로 집계
        // → totalPnL = positionPnL * 계좌_수 = 50 * 2 = 100
        var account1 = MakeAccount(id: 1);
        var account2 = MakeAccount(id: 2);
        var userSettings = new UserSettings { AccountSize = 50_000m };

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                    EntryPrice = 100m, CurrentPrice = 101m, Quantity = 100 },
            // UnrealizedPnL = +100
            new() { Symbol = "TSLA", Sector = "Auto",
                    EntryPrice = 200m, CurrentPrice = 199.5m, Quantity = 100 }
            // UnrealizedPnL = -50
            // positionPnL = 50
        };

        _accountManagerMock
            .Setup(m => m.GetAllAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManagedTradingAccount> { account1, account2 });
        _accountManagerMock
            .Setup(m => m.GetBrokerServiceForAccountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTrader.Services.Broker.IBrokerService?)null);
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);
        _positionStoreMock
            .Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var sut = CreateSut();

        // Act
        await sut.UpdateDailyPnLAsync();

        // Assert: 2개 계좌 각각 positionPnL=50을 집계 → totalPnL = 100
        // (향후 계좌별 포지션 필터링으로 변경 시 이 테스트를 업데이트해야 함)
        var portfolio = sut.GetPortfolioRiskState();
        portfolio.DailyPnL.Should().Be(100m);
        portfolio.OpenPositionCount.Should().Be(2);
    }
}

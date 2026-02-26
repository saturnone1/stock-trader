using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Risk;

namespace StockTrader.Tests;

public class RiskManagementServiceTests
{
    private readonly Mock<ITradeRepository> _tradeRepoMock;
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly TradingSettings _defaultSettings;

    public RiskManagementServiceTests()
    {
        _tradeRepoMock = new Mock<ITradeRepository>();
        _settingsRepoMock = new Mock<ISettingsRepository>();

        _defaultSettings = new TradingSettings
        {
            DefaultAccountSize = 100_000m,
            RiskPerTradePercent = 0.01m,
            DailyLossLimitPercent = 0.03m,
            MaxPositionsPerSector = 2,
            MaxTotalPositions = 10
        };
    }

    private RiskManagementService CreateSut(TradingSettings? settings = null)
    {
        var opts = Options.Create(settings ?? _defaultSettings);
        return new RiskManagementService(
            _tradeRepoMock.Object,
            _settingsRepoMock.Object,
            opts,
            NullLogger<RiskManagementService>.Instance);
    }

    // ────────────────────────────────────────────────────────────
    // CalculatePositionSize Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculatePositionSize_WithValidInputs_ReturnsCorrectSize()
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
    public void CalculatePositionSize_ZeroEntryPrice_ReturnsZero()
    {
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 0m,
            stopLossPrice: 95m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_ZeroStopLossPrice_ReturnsZero()
    {
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 0m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_StopLossEqualsEntry_ReturnsZero()
    {
        // stopLossPercent = 0 → division by zero → returns 0
        var sut = CreateSut();

        var result = sut.CalculatePositionSize(
            accountSize: 100_000m,
            riskPercent: 0.01m,
            entryPrice: 100m,
            stopLossPrice: 100m);

        result.Should().Be(0m);
    }

    [Fact]
    public void CalculatePositionSize_StopLossAboveEntry_StillCalculatesCorrectly()
    {
        // short position: entry=100, stopLoss=105
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
    // CanOpenPositionAsync Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanOpenPositionAsync_WhenTradingHalted_ReturnsFalse()
    {
        var sut = CreateSut();
        // UpdateDailyPnLAsync 를 먼저 호출해서 halted 상태 만들기
        // accountSize=100000, loss limit=3%=3000, 실제 손실=5000
        var settings = new UserSettings { AccountSize = 100_000m };
        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                EntryPrice = 100m, CurrentPrice = 95m, Quantity = 100 }
            // UnrealizedPnL = (95-100)*100 = -500... 3% of 100k = 3000 필요
        };

        // DailyPnLPercent <= -0.03 이어야 halted
        var lossPositions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                EntryPrice = 100m, CurrentPrice = 50m, Quantity = 100 }
            // UnrealizedPnL = -5000 → -5000/100000 = -5% → halted
        };

        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lossPositions);

        await sut.UpdateDailyPnLAsync();

        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        allowed.Should().BeFalse();
        reason.Should().Contain("Trading halted");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenMaxPositionsReached_ReturnsFalse()
    {
        var settings = new TradingSettings
        {
            MaxTotalPositions = 2,
            MaxPositionsPerSector = 5,
            DailyLossLimitPercent = 0.03m
        };
        var sut = CreateSut(settings);

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech" },
            new() { Symbol = "MSFT", Sector = "Tech" }
        };

        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        allowed.Should().BeFalse();
        reason.Should().Contain("Max total positions");
        reason.Should().Contain("2");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenSymbolAlreadyHasPosition_ReturnsFalse()
    {
        var sut = CreateSut();

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech" }
        };

        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "Tech");

        allowed.Should().BeFalse();
        reason.Should().Contain("Already have open position");
        reason.Should().Contain("AAPL");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenSectorLimitReached_ReturnsFalse()
    {
        var settings = new TradingSettings
        {
            MaxTotalPositions = 10,
            MaxPositionsPerSector = 2,
            DailyLossLimitPercent = 0.03m
        };
        var sut = CreateSut(settings);

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech" },
            new() { Symbol = "MSFT", Sector = "Tech" }
        };

        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var (allowed, reason) = await sut.CanOpenPositionAsync("GOOG", "Tech");

        allowed.Should().BeFalse();
        reason.Should().Contain("Max positions per sector");
        reason.Should().Contain("Tech");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenAllClear_ReturnsTrue()
    {
        var sut = CreateSut();

        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "Tech");

        allowed.Should().BeTrue();
        reason.Should().BeEmpty();
    }

    [Fact]
    public async Task CanOpenPositionAsync_EmptySector_SkipsSectorCheck()
    {
        // sector가 빈 문자열이면 섹터 체크 건너뜀
        var settings = new TradingSettings
        {
            MaxTotalPositions = 10,
            MaxPositionsPerSector = 1, // 매우 제한적
            DailyLossLimitPercent = 0.03m
        };
        var sut = CreateSut(settings);

        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        var (allowed, reason) = await sut.CanOpenPositionAsync("AAPL", "");

        allowed.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    // UpdateDailyPnLAsync Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDailyPnLAsync_WhenLossExceedsLimit_SetsIsTradingHalted()
    {
        var sut = CreateSut();
        var settings = new UserSettings { AccountSize = 100_000m };

        // 손실 5% > 한도 3%
        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                EntryPrice = 100m, CurrentPrice = 95m, Quantity = 100 },
            // UnrealizedPnL = (95-100)*100 = -500
            new() { Symbol = "TSLA", Sector = "Auto",
                EntryPrice = 200m, CurrentPrice = 155m, Quantity = 100 }
            // UnrealizedPnL = (155-200)*100 = -4500
            // Total = -5000 → -5% < -3% → HALTED
        };

        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        await sut.UpdateDailyPnLAsync();

        var state = await sut.GetCurrentRiskStateAsync();
        state.IsTradingHalted.Should().BeTrue();
        state.DailyPnL.Should().Be(-5000m);
        state.DailyPnLPercent.Should().Be(-0.05m);
    }

    [Fact]
    public async Task UpdateDailyPnLAsync_WhenLossWithinLimit_IsTradingHaltedFalse()
    {
        var sut = CreateSut();
        var settings = new UserSettings { AccountSize = 100_000m };

        // 손실 1% < 한도 3%
        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech",
                EntryPrice = 100m, CurrentPrice = 99m, Quantity = 100 }
            // UnrealizedPnL = -100 → -0.1%
        };

        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        await sut.UpdateDailyPnLAsync();

        var state = await sut.GetCurrentRiskStateAsync();
        state.IsTradingHalted.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDailyPnLAsync_TracksPositionsPerSector()
    {
        var sut = CreateSut();
        var settings = new UserSettings { AccountSize = 100_000m };

        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Sector = "Tech", EntryPrice = 100m, CurrentPrice = 105m, Quantity = 10 },
            new() { Symbol = "MSFT", Sector = "Tech", EntryPrice = 300m, CurrentPrice = 310m, Quantity = 5 },
            new() { Symbol = "JPM",  Sector = "Finance", EntryPrice = 150m, CurrentPrice = 152m, Quantity = 20 }
        };

        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        _tradeRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        await sut.UpdateDailyPnLAsync();

        var state = await sut.GetCurrentRiskStateAsync();
        state.PositionsPerSector["Tech"].Should().Be(2);
        state.PositionsPerSector["Finance"].Should().Be(1);
        state.OpenPositionCount.Should().Be(3);
    }

    [Fact]
    public async Task GetCurrentRiskStateAsync_InitialState_IsNotHalted()
    {
        var sut = CreateSut();

        var state = await sut.GetCurrentRiskStateAsync();

        state.IsTradingHalted.Should().BeFalse();
        state.DailyPnL.Should().Be(0m);
    }
}

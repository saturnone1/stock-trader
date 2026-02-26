using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Risk;
using StockTrader.Services.Signal;
using StockTrader.Services.Statistics;

namespace StockTrader.Tests;

public class SignalServiceTests
{
    private readonly Mock<IStatisticsService> _statsMock;
    private readonly Mock<IRiskManagementService> _riskMock;
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly TradingSettings _defaultSettings;

    public SignalServiceTests()
    {
        _statsMock = new Mock<IStatisticsService>();
        _riskMock = new Mock<IRiskManagementService>();
        _settingsRepoMock = new Mock<ISettingsRepository>();

        _defaultSettings = new TradingSettings
        {
            DefaultAccountSize = 100_000m,
            RiskPerTradePercent = 0.01m,
            DailyLossLimitPercent = 0.03m,
            MaxPositionsPerSector = 2,
            MaxTotalPositions = 10,
            MinExpectancy = 0m
        };
    }

    private SignalService CreateSut(TradingSettings? settings = null)
    {
        var opts = Options.Create(settings ?? _defaultSettings);
        return new SignalService(
            _statsMock.Object,
            _riskMock.Object,
            _settingsRepoMock.Object,
            opts,
            NullLogger<SignalService>.Instance);
    }

    private static PatternSignal CreateSignal(
        string symbol = "AAPL",
        PatternType patternType = PatternType.GapUpPullback,
        decimal entryPrice = 100m,
        decimal stopLossPrice = 95m,
        decimal targetPrice = 110m)
    {
        return new PatternSignal
        {
            Symbol = symbol,
            PatternType = patternType,
            EntryPrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TargetPrice = targetPrice,
            IsActive = true
        };
    }

    private static PatternStats CreateStats(
        decimal winRate = 0.6m,
        decimal avgWinPercent = 0.05m,
        decimal avgLossPercent = 0.03m)
    {
        return new PatternStats
        {
            PatternType = PatternType.GapUpPullback,
            WinRate = winRate,
            AvgWinPercent = avgWinPercent,
            AvgLossPercent = avgLossPercent,
            SampleSize = 50
        };
    }

    // ────────────────────────────────────────────────────────────
    // High expectancy, risk allowed → recommendation produced
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_HighExpectancyAndAllowed_ProducesRecommendation()
    {
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 95m, targetPrice: 110m);
        var stats = CreateStats(winRate: 0.6m, avgWinPercent: 0.05m, avgLossPercent: 0.02m);
        // Expectancy = 0.6*0.05 - 0.4*0.02 = 0.03 - 0.008 = 0.022 > 0

        var userSettings = new UserSettings { AccountSize = 100_000m, OrderMode = OrderMode.AlertOnly };

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                userSettings.AccountSize,
                _defaultSettings.RiskPerTradePercent,
                signal.EntryPrice,
                signal.StopLossPrice))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().HaveCount(1);
        var rec = result[0];
        rec.Symbol.Should().Be("AAPL");
        rec.EntryPrice.Should().Be(100m);
        rec.StopLossPrice.Should().Be(95m);
        rec.TargetPrice.Should().Be(110m);
        rec.WasExecuted.Should().BeFalse();
        rec.Mode.Should().Be(OrderMode.AlertOnly);
    }

    // ────────────────────────────────────────────────────────────
    // Low expectancy → filtered out
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_LowExpectancy_FiltersSignal()
    {
        // MinExpectancy = 0, Expectancy < 0 → filtered
        var settings = new TradingSettings
        {
            MinExpectancy = 0m,
            RiskPerTradePercent = 0.01m
        };
        var sut = CreateSut(settings);

        var signal = CreateSignal();
        var stats = CreateStats(winRate: 0.3m, avgWinPercent: 0.02m, avgLossPercent: 0.05m);
        // Expectancy = 0.3*0.02 - 0.7*0.05 = 0.006 - 0.035 = -0.029 < 0

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
        _riskMock.Verify(r => r.CanOpenPositionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_NullStats_FiltersSignal()
    {
        var sut = CreateSut();
        var signal = CreateSignal();

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatternStats?)null);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Risk blocks signal → no recommendation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_BlockedByRisk_NoRecommendation()
    {
        var sut = CreateSut();
        var signal = CreateSignal();
        var stats = CreateStats();

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Max total positions reached"));
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Position sizing and share quantity calculation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_CalculatesShareQuantityCorrectly()
    {
        // positionSize = 20000, entryPrice = 100 → shareQty = floor(20000/100) = 200
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 95m);
        var stats = CreateStats();

        var userSettings = new UserSettings { AccountSize = 100_000m };

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().HaveCount(1);
        result[0].ShareQuantity.Should().Be(200);
        result[0].PositionSize.Should().Be(20_000m);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_ShareQuantityIsFlooredNotRounded()
    {
        // positionSize = 15050, entryPrice = 100 → floor(150.5) = 150 (not 151)
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 95m);
        var stats = CreateStats();

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(15_050m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result[0].ShareQuantity.Should().Be(150);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_ZeroEntryPrice_ShareQuantityIsZero()
    {
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 0m, stopLossPrice: 0m);
        var stats = CreateStats();

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(0m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result[0].ShareQuantity.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_EmptySignalList_ReturnsEmptyList()
    {
        var sut = CreateSut();

        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateSignalsAsync_ExpectancySetInRecommendation()
    {
        var sut = CreateSut();

        var signal = CreateSignal();
        // Expectancy = 0.6*0.05 - 0.4*0.02 = 0.022
        var stats = CreateStats(winRate: 0.6m, avgWinPercent: 0.05m, avgLossPercent: 0.02m);

        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result[0].Expectancy.Should().Be(stats.Expectancy);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_MultipleSignals_EachEvaluatedIndependently()
    {
        var sut = CreateSut();

        var signal1 = CreateSignal("AAPL", entryPrice: 100m, stopLossPrice: 95m);
        var signal2 = CreateSignal("TSLA", entryPrice: 200m, stopLossPrice: 185m);

        var stats = CreateStats();
        var userSettings = new UserSettings { AccountSize = 100_000m };

        _statsMock.Setup(s => s.GetStatsAsync(It.IsAny<PatternType>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync("AAPL", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CanOpenPositionAsync("TSLA", "", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Max total positions reached"));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal1, signal2 });

        // AAPL은 통과, TSLA는 risk block
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
    }
}

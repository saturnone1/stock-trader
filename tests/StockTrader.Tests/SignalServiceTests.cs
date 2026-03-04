using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Risk;
using StockTrader.Services.Signal;
using StockTrader.Services.Statistics;

namespace StockTrader.Tests;

public class SignalServiceTests : IDisposable
{
    private readonly Mock<IStatisticsService> _statsMock;
    private readonly Mock<IRiskManagementService> _riskMock;
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly TradingSettings _defaultSettings;
    private readonly AppDbContext _db;

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

        // InMemory DB — 테스트별로 독립된 DB 인스턴스 사용
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private SignalService CreateSut(TradingSettings? settings = null)
    {
        var opts = Options.Create(settings ?? _defaultSettings);
        return new SignalService(
            _statsMock.Object,
            _riskMock.Object,
            _settingsRepoMock.Object,
            _db,
            opts,
            NullLogger<SignalService>.Instance);
    }

    private static PatternSignal CreateSignal(
        string symbol = "AAPL",
        PatternType patternType = PatternType.GapUpPullback,
        decimal entryPrice = 100m,
        decimal stopLossPrice = 95m,
        decimal targetPrice = 110m,
        decimal confidence = 0.8m)
    {
        return new PatternSignal
        {
            Symbol = symbol,
            PatternType = patternType,
            EntryPrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TargetPrice = targetPrice,
            Confidence = confidence,
            IsActive = true
        };
    }

    private static PatternStats CreateStats(
        decimal winRate = 0.6m,
        decimal avgWinPercent = 0.05m,
        decimal avgLossPercent = 0.03m,
        PatternType patternType = PatternType.GapUpPullback)
    {
        return new PatternStats
        {
            PatternType = patternType,
            WinRate = winRate,
            AvgWinPercent = avgWinPercent,
            AvgLossPercent = avgLossPercent,
            SampleSize = 50
        };
    }

    /// <summary>GetAllStatsAsync mock 설정 헬퍼</summary>
    private void SetupGetAllStats(params PatternStats[] statsItems)
    {
        var list = statsItems.ToList();
        _statsMock.Setup(s => s.GetAllStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);
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

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        // W02 fix: sector는 Ticker DB에서 조회 — DB에 없으면 symbol이 sector로 사용됨
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        SetupGetAllStats(stats);
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
    public async Task EvaluateSignalsAsync_NullStats_AllowsSignal()
    {
        // Gap 3 fix: 거래 이력 없는 패턴(stats==null)은 통과시켜서 신규 패턴도 거래 가능
        var sut = CreateSut();
        var signal = CreateSignal();

        // GetAllStatsAsync에 해당 패턴 통계 없음 → stats=null로 처리
        SetupGetAllStats(); // 빈 리스트
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatternStats?)null);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(5_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().HaveCount(1);
        result[0].Expectancy.Should().Be(0m);
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

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Max total positions reached"));
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // W02 fix: Ticker에 섹터 정보가 있으면 실제 섹터로 CanOpenPosition 호출
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_WithTickerSector_PassesCorrectSector()
    {
        var sut = CreateSut();
        var signal = CreateSignal(symbol: "AAPL");
        var stats = CreateStats();

        // Tickers DB에 AAPL = "Technology" 섹터 등록
        _db.Tickers.Add(new Ticker { Symbol = "AAPL", Sector = "Technology" });
        await _db.SaveChangesAsync();

        string? capturedSector = null;
        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync("AAPL", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((sym, sec, ct) => capturedSector = sec)
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(5_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        capturedSector.Should().Be("Technology");
    }

    [Fact]
    public async Task EvaluateSignalsAsync_WithoutTickerSector_FallsBackToSymbol()
    {
        // Ticker DB에 없으면 symbol 자체가 sector로 사용됨
        var sut = CreateSut();
        var signal = CreateSignal(symbol: "NVDA");
        var stats = CreateStats();

        string? capturedSector = null;
        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync("NVDA", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((sym, sec, ct) => capturedSector = sec)
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(5_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        capturedSector.Should().Be("NVDA"); // symbol fallback
    }

    // ────────────────────────────────────────────────────────────
    // Position sizing and share quantity calculation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_CalculatesShareQuantityCorrectly()
    {
        // positionSize = 20000 from risk calc, but capped at AccountSize/MaxTotalPositions = 100000/10 = 10000
        // entryPrice = 100 → shareQty = floor(10000/100) = 100
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 95m);
        var stats = CreateStats();

        var userSettings = new UserSettings { AccountSize = 100_000m };

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSettings);

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().HaveCount(1);
        // Position size capped: min(20000, 100000/10) = 10000
        result[0].ShareQuantity.Should().Be(100);
        result[0].PositionSize.Should().Be(10_000m);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_ShareQuantityIsFlooredNotRounded()
    {
        // positionSize = 8050, entryPrice = 100 → floor(80.5) = 80 (not 81)
        // Using size below cap (100000/10=10000) so cap doesn't interfere
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 95m);
        var stats = CreateStats();

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(8_050m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result[0].ShareQuantity.Should().Be(80);
    }

    [Fact]
    public async Task EvaluateSignalsAsync_ZeroEntryPrice_FilteredOut()
    {
        // 진입가=0, 손절=0 → 가격 유효성 위반(SL >= Entry)으로 필터링
        var sut = CreateSut();

        var signal = CreateSignal(entryPrice: 0m, stopLossPrice: 0m);

        SetupGetAllStats();
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
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

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(signal.PatternType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(20_000m);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result[0].Expectancy.Should().Be(stats.Expectancy);
    }

    // ────────────────────────────────────────────────────────────
    // Confidence filter → low confidence signals filtered
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_LowConfidence_FiltersSignal()
    {
        var sut = CreateSut();
        var signal = CreateSignal(confidence: 0.1m); // MinConfidence=0.3

        SetupGetAllStats();
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
        _riskMock.Verify(r => r.CanOpenPositionAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ────────────────────────────────────────────────────────────
    // Share qty = 0 → filtered (auto-trade cannot execute)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_ZeroShareQuantity_FiltersSignal()
    {
        // positionSize가 entryPrice보다 작으면 shareQty=0 → 필터링
        var sut = CreateSut();
        var signal = CreateSignal(entryPrice: 500m, stopLossPrice: 490m, targetPrice: 520m);
        var stats = CreateStats();

        SetupGetAllStats(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync(signal.Symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CalculatePositionSize(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(100m); // 100 < 500 → floor(100/500) = 0
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Invalid prices → filtered (SL >= Entry or Target <= Entry)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateSignalsAsync_InvalidPriceLevels_FiltersSignal()
    {
        var sut = CreateSut();
        // 손절가가 진입가 이상 → 유효하지 않은 시그널
        var signal = CreateSignal(entryPrice: 100m, stopLossPrice: 105m, targetPrice: 110m);

        SetupGetAllStats();
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { AccountSize = 100_000m });

        var result = await sut.EvaluateSignalsAsync(new List<PatternSignal> { signal });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateSignalsAsync_MultipleSignals_EachEvaluatedIndependently()
    {
        var sut = CreateSut();

        var signal1 = CreateSignal("AAPL", entryPrice: 100m, stopLossPrice: 95m);
        var signal2 = CreateSignal("TSLA", entryPrice: 200m, stopLossPrice: 185m);

        var stats = CreateStats();
        var userSettings = new UserSettings { AccountSize = 100_000m };

        SetupGetAllStats(stats);
        _statsMock.Setup(s => s.GetStatsAsync(It.IsAny<PatternType>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);
        _riskMock.Setup(r => r.CanOpenPositionAsync("AAPL", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
        _riskMock.Setup(r => r.CanOpenPositionAsync("TSLA", It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.ML;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class PatternDetectionServiceTests
{
    private readonly Mock<ISettingsRepository> _settingsRepoMock;
    private readonly Mock<IPatternStatsRepository> _statsRepoMock;
    private readonly Mock<ISignalScorer> _signalScorerMock;
    private readonly Mock<IMarketRegimeClassifier> _regimeClassifierMock;

    public PatternDetectionServiceTests()
    {
        _settingsRepoMock = new Mock<ISettingsRepository>();
        _statsRepoMock = new Mock<IPatternStatsRepository>();
        _signalScorerMock = new Mock<ISignalScorer>();
        _regimeClassifierMock = new Mock<IMarketRegimeClassifier>();

        // ML 모델 기본값: 비활성
        _signalScorerMock.Setup(s => s.IsModelLoaded).Returns(false);
        _regimeClassifierMock.Setup(r => r.IsModelLoaded).Returns(false);
    }

    /// <summary>
    /// SUT 생성 헬퍼. detectors가 없으면 빈 목록으로 생성.
    /// </summary>
    private PatternDetectionService CreateSut(IEnumerable<IPatternDetector>? detectors = null)
    {
        return new PatternDetectionService(
            detectors ?? Enumerable.Empty<IPatternDetector>(),
            _settingsRepoMock.Object,
            _statsRepoMock.Object,
            _signalScorerMock.Object,
            _regimeClassifierMock.Object,
            NullLogger<PatternDetectionService>.Instance);
    }

    /// <summary>
    /// 지정된 PatternType을 가진 Mock IPatternDetector를 생성한다.
    /// signal이 null이 아니면 DetectAsync 성공 반환, null이면 미감지.
    /// </summary>
    private static Mock<IPatternDetector> MakeDetector(
        PatternType patternType, PatternSignal? signal)
    {
        var mock = new Mock<IPatternDetector>();
        mock.Setup(d => d.PatternType).Returns(patternType);
        mock.Setup(d => d.DetectAsync(
                It.IsAny<string>(),
                It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(signal);
        return mock;
    }

    /// <summary>
    /// 지정 PatternType을 EnabledPatterns에 포함한 UserSettings를 세팅한다.
    /// </summary>
    private void SetupSettings(params PatternType[] enabledPatterns)
    {
        var settings = new UserSettings
        {
            EnabledPatterns = enabledPatterns.ToList()
        };
        _settingsRepoMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
    }

    private static OhlcvBar[] MakeBars(int count = 10, TimeFrame timeFrame = TimeFrame.Daily)
    {
        return Enumerable.Range(0, count)
            .Select(i => new OhlcvBar
            {
                Symbol = "AAPL",
                Timestamp = DateTime.UtcNow.AddDays(-i),
                TimeFrame = timeFrame,
                Open = 100m + i,
                High = 102m + i,
                Low = 99m + i,
                Close = 101m + i,
                Volume = 10_000L
            })
            .ToArray();
    }

    private static MarketRegime MakeRegime(bool spyAbove200Ma = true) =>
        new MarketRegime
        {
            SpyAbove200Ma = spyAbove200Ma,
            RegimeLabel = spyAbove200Ma ? "Bullish" : "Bearish",
            AsOf = DateTime.UtcNow
        };

    private static PatternSignal MakeSignal(string symbol, PatternType type, decimal confidence = 0.75m) =>
        new PatternSignal
        {
            Symbol = symbol,
            PatternType = type,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = 100m,
            StopLossPrice = 95m,
            TargetPrice = 110m,
            Confidence = confidence,
            IsActive = true
        };

    // ────────────────────────────────────────────────────────────
    // ScanSymbolAsync Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScanSymbol_NoEnabledPatterns_ReturnsEmpty()
    {
        // Arrange: EnabledPatterns가 비어있음 → 어떤 detector도 실행되지 않아야 함
        SetupSettings(/* 빈 목록 */);

        var detector = MakeDetector(
            PatternType.GapUpPullback,
            MakeSignal("AAPL", PatternType.GapUpPullback));

        var sut = CreateSut(new[] { detector.Object });
        var bars = MakeBars();
        var regime = MakeRegime();

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", bars, regime);

        // Assert
        signals.Should().BeEmpty();
        // DetectAsync는 호출되지 않아야 함
        detector.Verify(
            d => d.DetectAsync(It.IsAny<string>(), It.IsAny<OhlcvBar[]>(),
                               It.IsAny<MarketRegime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanSymbol_PatternDetected_ReturnsSignal()
    {
        // Arrange: GapUpPullback 활성화, detector가 signal 반환
        SetupSettings(PatternType.GapUpPullback);

        var expectedSignal = MakeSignal("AAPL", PatternType.GapUpPullback, confidence: 0.8m);
        var detector = MakeDetector(PatternType.GapUpPullback, expectedSignal);

        var sut = CreateSut(new[] { detector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert
        signals.Should().HaveCount(1);
        signals[0].Symbol.Should().Be("AAPL");
        signals[0].PatternType.Should().Be(PatternType.GapUpPullback);
    }

    [Fact]
    public async Task ScanSymbol_PatternNotDetected_ReturnsEmpty()
    {
        // Arrange: GapUpPullback 활성화, detector가 null 반환 (미감지)
        SetupSettings(PatternType.GapUpPullback);

        var detector = MakeDetector(PatternType.GapUpPullback, signal: null);
        var sut = CreateSut(new[] { detector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert
        signals.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanSymbol_MultiplePatterns_ReturnsAll()
    {
        // Arrange: 두 패턴 모두 활성화, 둘 다 signal 반환
        SetupSettings(PatternType.GapUpPullback, PatternType.Breakout);

        var gapSignal = MakeSignal("AAPL", PatternType.GapUpPullback, 0.7m);
        var breakoutSignal = MakeSignal("AAPL", PatternType.Breakout, 0.85m);

        var gapDetector = MakeDetector(PatternType.GapUpPullback, gapSignal);
        var breakoutDetector = MakeDetector(PatternType.Breakout, breakoutSignal);

        var sut = CreateSut(new[] { gapDetector.Object, breakoutDetector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: 두 시그널 모두 포함
        signals.Should().HaveCount(2);
        signals.Select(s => s.PatternType).Should()
            .Contain(PatternType.GapUpPullback).And
            .Contain(PatternType.Breakout);
    }

    [Fact]
    public async Task ScanSymbol_DetectorThrows_ContinuesOtherDetectors()
    {
        // Arrange: 첫 번째 detector가 예외를 던지고, 두 번째는 정상 동작
        SetupSettings(PatternType.GapUpPullback, PatternType.Breakout);

        var failingDetector = new Mock<IPatternDetector>();
        failingDetector.Setup(d => d.PatternType).Returns(PatternType.GapUpPullback);
        failingDetector.Setup(d => d.DetectAsync(
                It.IsAny<string>(), It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("데이터 부족"));

        var breakoutSignal = MakeSignal("AAPL", PatternType.Breakout, 0.9m);
        var breakoutDetector = MakeDetector(PatternType.Breakout, breakoutSignal);

        var sut = CreateSut(new[] { failingDetector.Object, breakoutDetector.Object });

        // Act: 예외가 전파되지 않아야 함
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: 실패한 detector는 건너뛰고 나머지 결과 반환
        signals.Should().HaveCount(1);
        signals[0].PatternType.Should().Be(PatternType.Breakout);
    }

    [Fact]
    public async Task ScanSymbol_MlModelLoaded_UsesClassifier()
    {
        // Arrange: ML 레짐 분류기가 모델 있음 → ClassifyAsync 호출 검증
        SetupSettings(PatternType.GapUpPullback);

        var mlRegime = new MarketRegime
        {
            SpyAbove200Ma = false,
            RegimeLabel = "ML-Bearish",
            MlClusterId = 2,
            MlRegimeLabel = "HighVolatility"
        };

        _regimeClassifierMock.Setup(r => r.IsModelLoaded).Returns(true);
        _regimeClassifierMock
            .Setup(r => r.ClassifyAsync(It.IsAny<OhlcvBar[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mlRegime);

        var signal = MakeSignal("AAPL", PatternType.GapUpPullback);
        // ML 분류기가 반환한 regime을 detector가 받아야 함
        var detector = new Mock<IPatternDetector>();
        detector.Setup(d => d.PatternType).Returns(PatternType.GapUpPullback);
        detector.Setup(d => d.DetectAsync(
                "AAPL",
                It.IsAny<OhlcvBar[]>(),
                It.Is<MarketRegime>(r => r.MlRegimeLabel == "HighVolatility"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(signal);

        var sut = CreateSut(new[] { detector.Object });
        var bars = MakeBars();
        var originalRegime = MakeRegime(spyAbove200Ma: true); // ML 결과와 다른 원본

        // Act
        await sut.ScanSymbolAsync("AAPL", bars, originalRegime);

        // Assert: ClassifyAsync가 호출됨
        _regimeClassifierMock.Verify(
            r => r.ClassifyAsync(bars, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanSymbol_MlModelNotLoaded_UsesOriginalRegime()
    {
        // Arrange: ML 분류기 모델 없음 → ClassifyAsync 호출하지 않음
        SetupSettings(PatternType.GapUpPullback);

        _regimeClassifierMock.Setup(r => r.IsModelLoaded).Returns(false);

        MarketRegime? capturedRegime = null;
        var detector = new Mock<IPatternDetector>();
        detector.Setup(d => d.PatternType).Returns(PatternType.GapUpPullback);
        detector.Setup(d => d.DetectAsync(
                It.IsAny<string>(), It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(), It.IsAny<CancellationToken>()))
            .Callback<string, OhlcvBar[], MarketRegime, CancellationToken>(
                (_, _, regime, _) => capturedRegime = regime)
            .ReturnsAsync((PatternSignal?)null);

        var sut = CreateSut(new[] { detector.Object });
        var originalRegime = MakeRegime(spyAbove200Ma: true);

        // Act
        await sut.ScanSymbolAsync("AAPL", MakeBars(), originalRegime);

        // Assert: ClassifyAsync 미호출, detector는 원본 regime을 받아야 함
        _regimeClassifierMock.Verify(
            r => r.ClassifyAsync(It.IsAny<OhlcvBar[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
        capturedRegime.Should().BeSameAs(originalRegime);
    }

    [Fact]
    public async Task ScanSymbol_SignalScorerLoaded_EnhancesConfidence()
    {
        // Arrange: SignalScorer 활성화 → Confidence가 ScoreAsync 반환값으로 교체됨
        SetupSettings(PatternType.GapUpPullback);

        const decimal originalConfidence = 0.6m;
        const decimal mlEnhancedConfidence = 0.88m;

        _signalScorerMock.Setup(s => s.IsModelLoaded).Returns(true);
        _signalScorerMock
            .Setup(s => s.ScoreAsync(
                It.IsAny<PatternSignal>(),
                It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mlEnhancedConfidence);

        // PatternStats 조회 → WinRate 반환
        _statsRepoMock
            .Setup(r => r.GetAsync(
                It.IsAny<PatternType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatternStats
            {
                PatternType = PatternType.GapUpPullback,
                WinRate = 0.65m,
                AvgWinPercent = 0.05m,
                AvgLossPercent = 0.02m
            });

        var signal = MakeSignal("AAPL", PatternType.GapUpPullback, confidence: originalConfidence);
        var detector = MakeDetector(PatternType.GapUpPullback, signal);

        var sut = CreateSut(new[] { detector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: Confidence가 ML 보정값으로 업데이트됨
        signals.Should().HaveCount(1);
        signals[0].Confidence.Should().Be(mlEnhancedConfidence);

        _signalScorerMock.Verify(
            s => s.ScoreAsync(
                It.IsAny<PatternSignal>(),
                It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanSymbol_SignalScorerNotLoaded_KeepsOriginalConfidence()
    {
        // Arrange: SignalScorer 비활성 → ScoreAsync 미호출, 원본 Confidence 유지
        SetupSettings(PatternType.GapUpPullback);

        _signalScorerMock.Setup(s => s.IsModelLoaded).Returns(false);

        const decimal originalConfidence = 0.72m;
        var signal = MakeSignal("AAPL", PatternType.GapUpPullback, confidence: originalConfidence);
        var detector = MakeDetector(PatternType.GapUpPullback, signal);

        var sut = CreateSut(new[] { detector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: Confidence 원본값 유지
        signals.Should().HaveCount(1);
        signals[0].Confidence.Should().Be(originalConfidence);

        _signalScorerMock.Verify(
            s => s.ScoreAsync(
                It.IsAny<PatternSignal>(), It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanSymbol_StatsNotFound_UsesDefaultWinRate()
    {
        // Arrange: PatternStats 없음 → WinRate 기본값 0.5로 ScoreAsync 호출
        SetupSettings(PatternType.Breakout);

        _signalScorerMock.Setup(s => s.IsModelLoaded).Returns(true);
        _signalScorerMock
            .Setup(s => s.ScoreAsync(
                It.IsAny<PatternSignal>(),
                It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(),
                0.5m, // 기본 WinRate
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0.7m);

        // Stats 없음 (null 반환)
        _statsRepoMock
            .Setup(r => r.GetAsync(
                PatternType.Breakout, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatternStats?)null);

        var signal = MakeSignal("AAPL", PatternType.Breakout, 0.6m);
        var detector = MakeDetector(PatternType.Breakout, signal);

        var sut = CreateSut(new[] { detector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: WinRate=0.5로 ScoreAsync가 호출됨
        signals.Should().HaveCount(1);
        _signalScorerMock.Verify(
            s => s.ScoreAsync(
                It.IsAny<PatternSignal>(),
                It.IsAny<OhlcvBar[]>(),
                It.IsAny<MarketRegime>(),
                0.5m,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanSymbol_DisabledPatternDetector_IsNotExecuted()
    {
        // Arrange: Breakout은 비활성화, GapUpPullback만 활성화
        SetupSettings(PatternType.GapUpPullback); // Breakout 미포함

        var gapDetector = MakeDetector(PatternType.GapUpPullback, MakeSignal("AAPL", PatternType.GapUpPullback));
        var breakoutDetector = MakeDetector(PatternType.Breakout, MakeSignal("AAPL", PatternType.Breakout));

        var sut = CreateSut(new[] { gapDetector.Object, breakoutDetector.Object });

        // Act
        var signals = await sut.ScanSymbolAsync("AAPL", MakeBars(), MakeRegime());

        // Assert: GapUpPullback만 실행, Breakout은 호출 안 됨
        signals.Should().HaveCount(1);
        signals[0].PatternType.Should().Be(PatternType.GapUpPullback);

        breakoutDetector.Verify(
            d => d.DetectAsync(It.IsAny<string>(), It.IsAny<OhlcvBar[]>(),
                               It.IsAny<MarketRegime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

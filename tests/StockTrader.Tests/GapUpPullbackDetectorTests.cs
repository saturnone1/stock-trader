using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class GapUpPullbackDetectorTests
{
    // Default config: MinGapPercent=0.02, MaxPullbackPercent=0.5, MinVolume=500000
    private static readonly GapUpPullbackConfig DefaultConfig = new()
    {
        MinGapPercent = 0.02m,
        MaxPullbackPercent = 0.5m,
        MinVolume = 500_000
    };

    private static GapUpPullbackDetector CreateSut(GapUpPullbackConfig? config = null)
    {
        var indicatorsMock = new Mock<IIndicatorService>();
        var settings = new PatternSettings
        {
            GapUpPullback = config ?? DefaultConfig
        };
        var snapshotMock = new Mock<IOptionsSnapshot<PatternSettings>>();
        snapshotMock.Setup(x => x.Value).Returns(settings);
        return new GapUpPullbackDetector(indicatorsMock.Object, snapshotMock.Object);
    }

    private static MarketRegime BullRegime() => new()
    {
        SpyAbove200Ma = true,
        SpyPrice = 500m,
        Spy200Ma = 450m,
        VixLevel = 15m
    };

    private static MarketRegime BearRegime() => new()
    {
        SpyAbove200Ma = false,
        SpyPrice = 400m,
        Spy200Ma = 450m,
        VixLevel = 30m
    };

    /// <summary>
    /// 유효한 GapUp+Pullback 패턴을 만드는 헬퍼.
    /// prev.Close=100, curr.Open=103 (3% gap), curr.High=106, curr.Close=105 (moderate pullback)
    /// pullbackFromHigh = (106 - 105) / (106 - 103) = 1/3 ≈ 0.333 → valid (0 &lt; 0.333 ≤ 0.5)
    /// </summary>
    private static OhlcvBar[] CreateValidBars(long volume = 600_000)
    {
        return new OhlcvBar[]
        {
            new() { Symbol = "AAPL", Open = 99m,  High = 101m, Low = 98m,  Close = 100m, Volume = 300_000 },
            new() { Symbol = "AAPL", Open = 103m, High = 106m, Low = 102m, Close = 105m, Volume = volume }
        };
    }

    // ────────────────────────────────────────────────────────────
    // 유효한 패턴 → 시그널 생성
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_ValidGapUpPullback_ReturnsSignal()
    {
        var sut = CreateSut();
        var bars = CreateValidBars();
        var regime = BullRegime();

        var result = await sut.DetectAsync("AAPL", bars, regime);

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("AAPL");
        result.PatternType.Should().Be(PatternType.GapUpPullback);
        result.IsActive.Should().BeTrue();
        result.EntryPrice.Should().Be(bars[^1].Close);
        result.StopLossPrice.Should().Be(bars[^1].Low);
    }

    [Fact]
    public async Task DetectAsync_ValidSignal_TargetIsTwiceTheRisk()
    {
        // target = close + (close - low) * 2
        var sut = CreateSut();
        var bars = CreateValidBars();
        var regime = BullRegime();

        var result = await sut.DetectAsync("AAPL", bars, regime);

        var curr = bars[^1];
        var expectedTarget = curr.Close + (curr.Close - curr.Low) * 2;
        result!.TargetPrice.Should().Be(expectedTarget);
    }

    // ────────────────────────────────────────────────────────────
    // 갭이 부족할 때 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_NoGapUp_ReturnsNull()
    {
        var sut = CreateSut();
        // prev.Close=100, curr.Open=101 → gap = 1% < 2% threshold
        var bars = new OhlcvBar[]
        {
            new() { Close = 100m },
            new() { Open = 101m, High = 105m, Low = 100m, Close = 101.5m, Volume = 600_000 }
        };

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 약세 시장 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_WeakRegime_ReturnsNull()
    {
        var sut = CreateSut();
        var bars = CreateValidBars();
        var regime = BearRegime();

        var result = await sut.DetectAsync("AAPL", bars, regime);

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 거래량 부족 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_InsufficientVolume_ReturnsNull()
    {
        var sut = CreateSut();
        // MinVolume=500000, 실제 volume=200000
        var bars = CreateValidBars(volume: 200_000);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 데이터 부족 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_InsufficientBars_ReturnsNull()
    {
        var sut = CreateSut();
        // bars.Length < 2
        var bars = new OhlcvBar[]
        {
            new() { Close = 100m, Volume = 600_000 }
        };

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_EmptyBars_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.DetectAsync("AAPL", Array.Empty<OhlcvBar>(), BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 풀백이 없을 때 (Close == High) → null
    // pullbackFromHigh = 0 → 진입 기회 없음
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_InsufficientPullback_ReturnsNull()
    {
        var sut = CreateSut();
        // prev.Close=100, curr.Open=103 (3% gap)
        // curr.High=106, curr.Close=106 → pullbackFromHigh = 0/3 = 0 → no pullback → null
        var bars = new OhlcvBar[]
        {
            new() { Close = 100m },
            new() { Open = 103m, High = 106m, Low = 102m, Close = 106m, Volume = 600_000 }
        };

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // Confidence 는 0~1 범위
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_Confidence_IsBetweenZeroAndOne()
    {
        var sut = CreateSut();
        var bars = CreateValidBars();

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().NotBeNull();
        result!.Confidence.Should().BeInRange(0m, 1m);
    }

    // ────────────────────────────────────────────────────────────
    // PatternType 속성 확인
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void PatternType_IsGapUpPullback()
    {
        var sut = CreateSut();
        sut.PatternType.Should().Be(PatternType.GapUpPullback);
    }

    // ────────────────────────────────────────────────────────────
    // curr.Open == curr.High 엣지 케이스 (pullback division by zero 방지)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_OpenEqualsHigh_DoesNotThrow()
    {
        var sut = CreateSut();
        // curr.High == curr.Open → pullbackFromHigh 계산 시 분모=0
        // 코드 상 분모 0이면 pullbackFromHigh=0 → MaxPullbackPercent(0.5) 미만 → null 반환
        var bars = new OhlcvBar[]
        {
            new() { Close = 100m },
            new() { Open = 103m, High = 103m, Low = 100m, Close = 101m, Volume = 600_000 }
        };

        var act = async () => await sut.DetectAsync("AAPL", bars, BullRegime());

        await act.Should().NotThrowAsync();
    }
}

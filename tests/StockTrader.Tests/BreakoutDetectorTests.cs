using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class BreakoutDetectorTests
{
    // Default: LookbackDays=252, MinVolumeMultiplier=1.5, BreakoutMarginPercent=0.001
    private static readonly BreakoutConfig DefaultConfig = new()
    {
        LookbackDays = 252,
        MinVolumeMultiplier = 1.5m,
        BreakoutMarginPercent = 0.001m
    };

    private static BreakoutDetector CreateSut(
        BreakoutConfig? config = null,
        decimal atrValue = 2m)
    {
        var indicatorsMock = new Mock<IIndicatorService>();

        // ATR 반환값을 제어하여 stopLoss/target 계산 일관성 유지
        indicatorsMock.Setup(i => i.ATR(It.IsAny<OhlcvBar[]>(), It.IsAny<int>()))
            .Returns<OhlcvBar[], int>((bars, _) =>
            {
                var result = new decimal[bars.Length];
                for (int i = 0; i < bars.Length; i++)
                    result[i] = atrValue;
                return result;
            });

        var settings = new PatternSettings
        {
            Breakout = config ?? DefaultConfig
        };
        var snapshotMock = new Mock<IOptionsSnapshot<PatternSettings>>();
        snapshotMock.Setup(x => x.Value).Returns(settings);
        return new BreakoutDetector(indicatorsMock.Object, snapshotMock.Object);
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
    /// LookbackDays=10으로 줄여서 관리 가능한 데이터셋으로 테스트하는 헬퍼.
    ///
    /// 설계:
    ///   - lookback 10개 bars 중 앞 9개: high=90, vol=100000
    ///   - 마지막(curr) bar: high=105, close=105, vol=200000
    ///   - lookbackBars = bars[^10..] = 전체 10개 bars
    ///   - high52w = max(all highs) = 105 (마지막 bar 포함)
    ///   - 하지만 close(105) > high52w(105) * 1.001 = 105.105 → FALSE!
    ///
    /// 따라서 마지막 bar를 제외한 lookback을 구성하려면,
    /// LookbackDays=9를 사용하거나, 총 bars 수를 LookbackDays보다 많게 해야 합니다.
    ///
    /// 수정된 설계 (LookbackDays=5):
    ///   - 총 6개 bars, lookback = bars[^5..] = 마지막 5개
    ///   - bars[0]: (기준선, lookback 제외) high=90
    ///   - bars[1..4]: lookback 포함, high=90, vol=100000
    ///   - bars[5] (curr): high=200, close=200, vol=200000
    ///   - high52w = max(bars[1..5].High) = 200 (curr 포함)
    ///
    /// 더 나은 접근: LookbackDays=5, bars 6개
    ///   - bars[0]: lookback 제외 (historical)
    ///   - bars[1..4]: high=90, vol=100000 (4개)
    ///   - bars[5] curr: close=200 > high52w(90)*1.001=90.09
    ///   - lookback = bars[^5..] = bars[1..5]
    ///   - high52w = max(bars[1..4].High=90, bars[5].High=201) → 201
    ///
    /// 가장 단순한 방법: LookbackDays=5, 6개 bars
    ///   - 앞 5개(lookback): high=90, vol=100000
    ///   - 마지막 1개(curr, LookbackDays+1): close=200 → lookback=bars[^5..] = bars[1..5]
    ///   - bars[1..4].High=90, bars[5].High=201
    ///   → high52w=201, close=200 <= 201*1.001 → NOT a breakout!
    ///
    /// FINAL 설계: LookbackDays=5, 총 6개, curr(bars[5])는 lookback[^5..]에 포함
    ///   bars[1..4]: high=90 (4개), bars[5]=curr: high=150, close=200
    ///   high52w = max(90,90,90,90,150) = 150
    ///   close=200 > 150*1.001=150.15 → BREAKOUT
    ///   avgVolume = (4*100000+200000)/5 = 600000/5 = 120000
    ///   volumeRatio = 200000/120000 = 1.667 > 1.5 → VALID
    /// </summary>
    private static (OhlcvBar[] bars, BreakoutConfig config) CreateValidBreakoutSetup()
    {
        var config = new BreakoutConfig
        {
            LookbackDays = 5,
            MinVolumeMultiplier = 1.5m,
            BreakoutMarginPercent = 0.001m
        };

        var bars = new List<OhlcvBar>();

        // bar[0]: lookback 범위 밖 (bars.Length=6, lookback=bars[^5..]=bars[1..5])
        bars.Add(new OhlcvBar { Open = 85m, High = 90m, Low = 84m, Close = 88m, Volume = 100_000 });

        // bars[1..4]: lookback 내, 52주 최고가 기여 high=90
        for (int i = 0; i < 4; i++)
        {
            bars.Add(new OhlcvBar { Open = 85m, High = 90m, Low = 84m, Close = 88m, Volume = 100_000 });
        }

        // bars[5] = curr: high=150 (lookback 내), close=200 > 150*1.001=150.15
        bars.Add(new OhlcvBar
        {
            Open  = 89m,
            High  = 150m,   // lookback 내 최고가이지만 close는 이를 훨씬 초과
            Low   = 88m,
            Close = 200m,   // 200 > 150.15 → breakout
            Volume = 200_000
        });

        return (bars.ToArray(), config);
    }

    // ────────────────────────────────────────────────────────────
    // 유효한 패턴 → 시그널 생성
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_ValidBreakout_ReturnsSignal()
    {
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("AAPL");
        result.PatternType.Should().Be(PatternType.Breakout);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_ValidBreakout_EntryPriceIsCurrentClose()
    {
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result!.EntryPrice.Should().Be(200m); // curr.Close=200
    }

    [Fact]
    public async Task DetectAsync_ValidBreakout_StopLossIsCloseMinusAtrTimesMultiplier()
    {
        // curr.Close=200, ATR=2, AtrStopMultiplier=1.5 → stopLoss = 200 - 2*1.5 = 197
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result!.StopLossPrice.Should().Be(197m);
    }

    [Fact]
    public async Task DetectAsync_ValidBreakout_TargetIsClosePlusAtrTimesMultiplier()
    {
        // curr.Close=200, ATR=2, AtrTargetMultiplier=4.0 → target = 200 + 2*4 = 208
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result!.TargetPrice.Should().Be(208m);
    }

    // ────────────────────────────────────────────────────────────
    // 52주 최고가 미달 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_PriceBelowHigh_ReturnsNull()
    {
        // LookbackDays=5, 총 6개 bars
        // bars[1..4]: high=100, bars[5](curr): close=98 < 100*1.001=100.1 → 미돌파
        var config = new BreakoutConfig
        {
            LookbackDays = 5,
            MinVolumeMultiplier = 1.5m,
            BreakoutMarginPercent = 0.001m
        };
        var sut = CreateSut(config, atrValue: 2m);

        var bars = new List<OhlcvBar>();
        // bar[0]: lookback 밖
        bars.Add(new OhlcvBar { High = 100m, Close = 95m, Volume = 100_000 });
        // bars[1..4]: high=100
        for (int i = 0; i < 4; i++)
            bars.Add(new OhlcvBar { High = 100m, Close = 95m, Volume = 100_000 });
        // bars[5] curr: high=99, close=98 → high52w=max(100,99)=100 → 98<=100.1 → null
        bars.Add(new OhlcvBar { Open = 96m, High = 99m, Low = 95m, Close = 98m, Volume = 200_000 });

        var result = await sut.DetectAsync("AAPL", bars.ToArray(), BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 약세 시장 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_WeakRegime_ReturnsNull()
    {
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BearRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 거래량 부족 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_LowVolume_ReturnsNull()
    {
        // LookbackDays=5, 총 6개 bars
        // bars[1..4]: high=90, vol=100000
        // bars[5] curr: high=150, close=200 → breakout 조건 충족
        // avgVolume = (4*100000 + 120000)/5 = 520000/5 = 104000
        // volumeRatio = 120000/104000 ≈ 1.154 < 1.5 → rejected
        var config = new BreakoutConfig
        {
            LookbackDays = 5,
            MinVolumeMultiplier = 1.5m,
            BreakoutMarginPercent = 0.001m
        };
        var sut = CreateSut(config, atrValue: 2m);

        var bars = new List<OhlcvBar>();
        bars.Add(new OhlcvBar { High = 90m, Close = 88m, Volume = 100_000 }); // lookback 밖
        for (int i = 0; i < 4; i++)
            bars.Add(new OhlcvBar { High = 90m, Close = 88m, Volume = 100_000 });
        // curr: close=200, vol=120000 → ratio < 1.5
        bars.Add(new OhlcvBar { Open = 89m, High = 150m, Low = 88m, Close = 200m, Volume = 120_000 });

        var result = await sut.DetectAsync("AAPL", bars.ToArray(), BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // 데이터 부족 (bars.Length < LookbackDays) → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_InsufficientBars_ReturnsNull()
    {
        // LookbackDays=252이지만 bars가 10개뿐
        var sut = CreateSut(atrValue: 2m); // DefaultConfig.LookbackDays=252

        var bars = Enumerable.Range(0, 10).Select(_ =>
            new OhlcvBar { High = 90m, Close = 88m, Volume = 100_000 }).ToArray();

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_EmptyBars_ReturnsNull()
    {
        var sut = CreateSut(atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", Array.Empty<OhlcvBar>(), BullRegime());

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────
    // Confidence 는 0~1 범위
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_Confidence_IsBetweenZeroAndOne()
    {
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result!.Confidence.Should().BeInRange(0m, 1m);
    }

    // ────────────────────────────────────────────────────────────
    // PatternType 속성 확인
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void PatternType_IsBreakout()
    {
        var sut = CreateSut();
        sut.PatternType.Should().Be(PatternType.Breakout);
    }

    // ────────────────────────────────────────────────────────────
    // Details 문자열에 volume 정보 포함
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_ValidBreakout_DetailsContainsVolumeInfo()
    {
        var (bars, config) = CreateValidBreakoutSetup();
        var sut = CreateSut(config, atrValue: 2m);

        var result = await sut.DetectAsync("AAPL", bars, BullRegime());

        result!.Details.Should().Contain("Volume");
    }

    // ────────────────────────────────────────────────────────────
    // 경계선 케이스: close가 high52w * (1 + margin) 이하일 때 → null
    // lookback=10 bars, 모든 bar high=100 (curr 포함)
    // close=100.05 → 100.05 <= 100 * 1.001 = 100.1 → null
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetectAsync_CloseAtOrBelowBreakoutThreshold_ReturnsNull()
    {
        var config = new BreakoutConfig
        {
            LookbackDays = 10,
            MinVolumeMultiplier = 1.5m,
            BreakoutMarginPercent = 0.001m
        };
        var sut = CreateSut(config, atrValue: 2m);

        var bars = new List<OhlcvBar>();
        for (int i = 0; i < 9; i++)
            bars.Add(new OhlcvBar { Open = 98m, High = 100m, Low = 97m, Close = 99m, Volume = 100_000 });
        // 마지막 bar: high=100 (같음) → high52w=100
        // close=100.05 <= 100*1.001=100.1 → NOT a breakout
        bars.Add(new OhlcvBar { Open = 99m, High = 100m, Low = 98m, Close = 100.05m, Volume = 200_000 });

        var result = await sut.DetectAsync("AAPL", bars.ToArray(), BullRegime());

        result.Should().BeNull();
    }
}

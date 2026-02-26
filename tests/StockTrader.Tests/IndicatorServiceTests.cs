using FluentAssertions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;

namespace StockTrader.Tests;

public class IndicatorServiceTests
{
    private readonly IndicatorService _sut;

    public IndicatorServiceTests()
    {
        _sut = new IndicatorService();
    }

    // ────────────────────────────────────────────────────────────
    // SMA Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void SMA_WithKnownValues_ReturnsExpectedResults()
    {
        // [1,2,3,4,5] period=3
        // index 0: warm-up → 0
        // index 1: warm-up → 0
        // index 2: (1+2+3)/3 = 2
        // index 3: (2+3+4)/3 = 3
        // index 4: (3+4+5)/3 = 4
        var closes = new decimal[] { 1, 2, 3, 4, 5 };

        var result = _sut.SMA(closes, period: 3);

        result.Should().HaveCount(5);
        result[0].Should().Be(0m);
        result[1].Should().Be(0m);
        result[2].Should().Be(2m);
        result[3].Should().Be(3m);
        result[4].Should().Be(4m);
    }

    [Fact]
    public void SMA_PeriodLargerThanData_ReturnsAllZeros()
    {
        var closes = new decimal[] { 10, 20, 30 };

        var result = _sut.SMA(closes, period: 5);

        result.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public void SMA_EmptyArray_ReturnsEmptyArray()
    {
        var result = _sut.SMA(Array.Empty<decimal>(), period: 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public void SMA_PeriodEqualsDataLength_OnlyLastElementNonZero()
    {
        var closes = new decimal[] { 10, 20, 30 };

        var result = _sut.SMA(closes, period: 3);

        result[0].Should().Be(0m);
        result[1].Should().Be(0m);
        result[2].Should().Be(20m); // (10+20+30)/3
    }

    // ────────────────────────────────────────────────────────────
    // EMA Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void EMA_FirstValueEqualsFirstClose()
    {
        var closes = new decimal[] { 50, 52, 48, 55, 53 };

        var result = _sut.EMA(closes, period: 3);

        result[0].Should().Be(50m);
    }

    [Fact]
    public void EMA_EmptyArray_ReturnsEmptyArray()
    {
        var result = _sut.EMA(Array.Empty<decimal>(), period: 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public void EMA_SingleElement_ReturnsThatElement()
    {
        var closes = new decimal[] { 100m };

        var result = _sut.EMA(closes, period: 3);

        result.Should().HaveCount(1);
        result[0].Should().Be(100m);
    }

    [Fact]
    public void EMA_ReturnsCorrectCount()
    {
        var closes = new decimal[] { 10, 20, 30, 40, 50 };

        var result = _sut.EMA(closes, period: 3);

        result.Should().HaveCount(5);
    }

    [Fact]
    public void EMA_ValuesReactToRecentPriceChange()
    {
        // EMA는 최신 가격에 더 민감하게 반응해야 한다
        var closesTrending = new decimal[] { 10, 20, 30, 40, 50 };
        var closesFalling  = new decimal[] { 50, 40, 30, 20, 10 };

        var emaTrend = _sut.EMA(closesTrending, period: 3);
        var emaFall  = _sut.EMA(closesFalling, period: 3);

        emaTrend[^1].Should().BeGreaterThan(emaFall[^1]);
    }

    // ────────────────────────────────────────────────────────────
    // RSI Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void RSI_AllGains_Returns100AtPeriodIndex()
    {
        // 모든 변화가 상승이면 RSI = 100
        // period=14이므로 15개 이상 데이터 필요 (closes.Length > period)
        var closes = Enumerable.Range(1, 16).Select(i => (decimal)i).ToArray();

        var result = _sut.RSI(closes, period: 14);

        result[14].Should().Be(100m);
    }

    [Fact]
    public void RSI_InsufficientData_ReturnsAllZeros()
    {
        // closes.Length <= period이면 전부 0 반환
        var closes = new decimal[] { 10, 11, 12, 13, 14 };

        var result = _sut.RSI(closes, period: 14);

        result.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public void RSI_ValidData_ValuesBetween0And100()
    {
        var closes = new decimal[]
        {
            44.34m, 44.09m, 44.15m, 43.61m, 44.33m,
            44.83m, 45.10m, 45.15m, 43.61m, 44.33m,
            44.83m, 45.10m, 45.15m, 45.23m, 45.25m,
            45.00m, 44.50m, 44.75m, 45.10m, 44.80m
        };

        var result = _sut.RSI(closes, period: 14);

        foreach (var v in result.Where(v => v != 0))
        {
            v.Should().BeInRange(0m, 100m);
        }
    }

    [Fact]
    public void RSI_EmptyArray_ReturnsEmptyArray()
    {
        var result = _sut.RSI(Array.Empty<decimal>(), period: 14);

        result.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // BollingerBands Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void BollingerBands_MiddleBandEqualsSMA()
    {
        var closes = Enumerable.Range(1, 25).Select(i => (decimal)i).ToArray();
        int period = 20;

        var (upper, middle, lower) = _sut.BollingerBands(closes, period: period);
        var sma = _sut.SMA(closes, period: period);

        for (int i = 0; i < closes.Length; i++)
        {
            middle[i].Should().Be(sma[i],
                because: $"BollingerBands middle[{i}] should equal SMA[{i}]");
        }
    }

    [Fact]
    public void BollingerBands_UpperAboveAndLowerBelowMiddle()
    {
        var closes = Enumerable.Range(1, 25).Select(i => (decimal)i).ToArray();

        var (upper, middle, lower) = _sut.BollingerBands(closes, period: 20);

        for (int i = 19; i < closes.Length; i++)
        {
            upper[i].Should().BeGreaterThan(middle[i]);
            lower[i].Should().BeLessThan(middle[i]);
        }
    }

    [Fact]
    public void BollingerBands_ConstantPrices_UpperEqualsLowerEqualsMiddle()
    {
        // 가격이 모두 같으면 표준편차=0 → upper=lower=middle
        var closes = Enumerable.Repeat(50m, 25).ToArray();

        var (upper, middle, lower) = _sut.BollingerBands(closes, period: 20);

        for (int i = 19; i < closes.Length; i++)
        {
            upper[i].Should().Be(middle[i]);
            lower[i].Should().Be(middle[i]);
        }
    }

    [Fact]
    public void BollingerBands_EmptyArray_ReturnsEmptyArrays()
    {
        var (upper, middle, lower) = _sut.BollingerBands(Array.Empty<decimal>());

        upper.Should().BeEmpty();
        middle.Should().BeEmpty();
        lower.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // VWAP Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void VWAP_WithKnownBars_ReturnsExpectedValues()
    {
        // Bar1: H=105, L=95, C=100, Vol=1000  → TP=100, TPV=100000
        // Bar2: H=110, L=100, C=108, Vol=2000  → TP=106, TPV=212000
        // Cumulative after bar1: VWAP = 100000/1000 = 100
        // Cumulative after bar2: VWAP = 312000/3000 = 104
        var bars = new OhlcvBar[]
        {
            new() { High = 105m, Low = 95m, Close = 100m, Volume = 1000 },
            new() { High = 110m, Low = 100m, Close = 108m, Volume = 2000 }
        };

        var result = _sut.VWAP(bars);

        result[0].Should().Be(100m);
        result[1].Should().Be(104m);
    }

    [Fact]
    public void VWAP_EmptyArray_ReturnsEmptyArray()
    {
        var result = _sut.VWAP(Array.Empty<OhlcvBar>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void VWAP_ZeroVolume_ReturnsZero()
    {
        var bars = new OhlcvBar[]
        {
            new() { High = 110m, Low = 90m, Close = 100m, Volume = 0 }
        };

        var result = _sut.VWAP(bars);

        result[0].Should().Be(0m);
    }

    [Fact]
    public void VWAP_ResultLengthMatchesInputLength()
    {
        var bars = new OhlcvBar[]
        {
            new() { High = 105m, Low = 95m, Close = 100m, Volume = 1000 },
            new() { High = 108m, Low = 98m, Close = 105m, Volume = 2000 },
            new() { High = 112m, Low = 102m, Close = 110m, Volume = 1500 }
        };

        var result = _sut.VWAP(bars);

        result.Should().HaveCount(3);
    }

    // ────────────────────────────────────────────────────────────
    // ATR Tests
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void ATR_WithKnownBars_FirstBarTrueRangeIsHighMinusLow()
    {
        // bar[0]의 true range = High - Low (이전 종가 없으므로)
        var bars = CreateUniformBars(count: 15, open: 100m, high: 110m, low: 90m, close: 105m);

        var result = _sut.ATR(bars, period: 14);

        // period=14이면 bars[13]부터 ATR 값이 채워짐
        // 모든 TR이 동일하므로 ATR = 20
        result[13].Should().Be(20m);
    }

    [Fact]
    public void ATR_EmptyArray_ReturnsEmptyArray()
    {
        var result = _sut.ATR(Array.Empty<OhlcvBar>(), period: 14);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ATR_SingleBar_ReturnsArrayWithOneZeroElement()
    {
        var bars = new OhlcvBar[]
        {
            new() { High = 110m, Low = 90m, Close = 100m, Open = 95m }
        };

        var result = _sut.ATR(bars, period: 14);

        // bars.Length <= 1이면 빈 배열(모두 0)
        result.Should().HaveCount(1);
        result[0].Should().Be(0m);
    }

    [Fact]
    public void ATR_ResultLengthMatchesInputLength()
    {
        var bars = CreateUniformBars(count: 20, open: 100m, high: 110m, low: 90m, close: 105m);

        var result = _sut.ATR(bars, period: 14);

        result.Should().HaveCount(20);
    }

    [Fact]
    public void ATR_WithGap_TrueRangeConsidersPreviousClose()
    {
        // bar0: H=110, L=100, C=105
        // bar1: gap up: O=120, H=130, L=118, C=125
        // TR(bar1) = max(130-118, |130-105|, |118-105|) = max(12, 25, 13) = 25
        // 하지만 period=14이므로 bars[1] ATR은 아직 warm-up 단계
        // 충분한 bars를 줘서 ATR을 확인
        var bars = new List<OhlcvBar>();
        bars.Add(new OhlcvBar { Open = 100m, High = 110m, Low = 100m, Close = 105m });
        bars.Add(new OhlcvBar { Open = 120m, High = 130m, Low = 118m, Close = 125m });
        // period=2로 테스트하면 bar[1]이 ATR 첫 확정 값
        // TR[0] = 10, TR[1] = 25 → ATR[1] = (10+25)/2 = 17.5
        var barsArr = bars.ToArray();

        var result = _sut.ATR(barsArr, period: 2);

        result[1].Should().Be(17.5m);
    }

    // ────────────────────────────────────────────────────────────
    // Helper
    // ────────────────────────────────────────────────────────────

    private static OhlcvBar[] CreateUniformBars(int count,
        decimal open, decimal high, decimal low, decimal close)
    {
        return Enumerable.Range(0, count).Select(_ => new OhlcvBar
        {
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 100_000
        }).ToArray();
    }
}

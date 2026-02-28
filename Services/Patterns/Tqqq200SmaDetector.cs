using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// TQQQ 200-SMA Rotation Strategy (아기티큐 전략 개선판).
///
/// 원본 전략:
///   - TQQQ 자체 SMA(200) 기준 4구간 판별 (하락/돌파/집중투자/과열)
///   - 이틀 연속 종가 > SMA200 확인 후 풀매수
///   - 고정 익절(+10/25/50%), 고정 손절(-5%)
///   - 백테스트: 승률 30%, 손익비 7.75, 기댓값 +6.5%/거래
///
/// 개선 사항 (자동화 최적화):
///   1. EMA(50) 골든크로스 필터 → 휩쏘 감소
///   2. 거래량 확인 → 허위 돌파 필터링
///   3. ATR 적응형 손절 → 고정 -5% 대신 시장 변동성 반영
///   4. 고정 익절 제거 → 넓은 목표가 + BacktestService 트레일링 스탑에 위임 (추세 수익 극대화)
///   5. 과열 구간(SMA200+5%↑) 진입 억제 → 고점 추격 방지
///   6. SMA200을 지지선으로 활용한 손절 최적화
///
/// 진입 시나리오:
///   A. 정식 진입: SMA200 아래→위 크로스오버 + N일 연속 확인 + 거래량
///   B. 밴드 진입: 상승추세(EMA50>SMA200) 중 과열→집중투자 구간 되돌림 (눌림목)
/// </summary>
public class Tqqq200SmaDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly Tqqq200SmaConfig _config;

    public PatternType PatternType => PatternType.Tqqq200Sma;

    public Tqqq200SmaDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.Tqqq200Sma;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        var minBars = _config.SmaPeriod + _config.ConfirmationDays + 5;
        if (bars.Length < minBars)
            return Task.FromResult<PatternSignal?>(null);

        var closes = bars.Select(b => b.Close).ToArray();
        var i = bars.Length - 1;
        var curr = bars[i];

        if (curr.Close <= 0 || curr.Open <= 0)
            return Task.FromResult<PatternSignal?>(null);

        // ── 지표 계산 ──
        var sma200 = _indicators.SMA(closes, _config.SmaPeriod);
        var ema50 = _indicators.EMA(closes, _config.ShortTrendEmaPeriod);
        var atr = _indicators.ATR(bars);

        if (sma200[i] <= 0 || ema50[i] <= 0 || atr[i] <= 0)
            return Task.FromResult<PatternSignal?>(null);

        var smaValue = sma200[i];
        var overheatLine = smaValue * (1 + _config.OverheatPercent);

        // ── 기본 조건: 현재 종가 > SMA200 (집중투자 구간 이상) ──
        if (curr.Close <= smaValue)
            return Task.FromResult<PatternSignal?>(null);

        // ── 과열 구간 진입 차단 (SMA200 + 5% 초과) ──
        if (curr.Close > overheatLine)
            return Task.FromResult<PatternSignal?>(null);

        // ── 진입 시나리오 판별 ──
        decimal confidence;
        string entryType;

        bool isBreakoutEntry = IsBreakoutEntry(closes, sma200, i);
        bool isBandEntry = IsBandEntry(closes, sma200, ema50, i);

        if (isBreakoutEntry)
        {
            // 시나리오 A: 정식 진입 (SMA200 크로스오버 + N일 연속 확인)
            entryType = "정식진입";
            confidence = 0.70m;

            // 골든크로스(EMA50 > SMA200) 보너스
            if (ema50[i] > smaValue)
                confidence += 0.10m;
        }
        else if (isBandEntry)
        {
            // 시나리오 B: 밴드 진입 (상승추세 중 눌림목)
            // EMA50 > SMA200 필수 (골든크로스 = 확립된 상승추세)
            if (ema50[i] <= smaValue)
                return Task.FromResult<PatternSignal?>(null);

            entryType = "밴드진입";
            confidence = 0.55m;

            // SMA200에 가까울수록 높은 신뢰도 (더 좋은 진입가)
            var distFromSma = (curr.Close - smaValue) / smaValue;
            confidence += Math.Max(0, (0.05m - distFromSma) * 3m);
        }
        else
        {
            return Task.FromResult<PatternSignal?>(null);
        }

        // ── 거래량 확인 ──
        if (!HasVolumeConfirmation(bars, i))
        {
            confidence -= 0.15m;
            if (confidence < 0.40m)
                return Task.FromResult<PatternSignal?>(null);
        }

        // ── ATR 기반 손절/목표 (적응형) ──
        var currentAtr = atr[i];
        var stopLoss = curr.Close - currentAtr * _config.AtrStopMultiplier;
        var target = curr.Close + currentAtr * _config.AtrTargetMultiplier;

        // SMA200을 지지선으로 활용: 손절이 SMA200보다 크게 아래면 SMA200 바로 아래로 조정
        var smaStop = smaValue * 0.99m;
        if (stopLoss < smaStop)
            stopLoss = smaStop;

        confidence = Math.Round(Math.Min(1.0m, Math.Max(0.40m, confidence)), 2);

        var distAboveSma = (curr.Close - smaValue) / smaValue;

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.Tqqq200Sma,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = Math.Round(stopLoss, 2),
            TargetPrice = Math.Round(target, 2),
            Confidence = confidence,
            Details = $"[{entryType}] SMA200=${smaValue:F2}, EMA50=${ema50[i]:F2}, " +
                      $"SMA위={distAboveSma:P1}, 과열선=${overheatLine:F2}, ATR=${currentAtr:F2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }

    /// <summary>
    /// 정식 진입: 최근 N일 이전에는 SMA200 아래였고, 최근 N일 연속 SMA200 위 (크로스오버).
    /// </summary>
    private bool IsBreakoutEntry(decimal[] closes, decimal[] sma200, int currentIndex)
    {
        int confirmDays = _config.ConfirmationDays;

        // 최근 N일 연속 종가 > SMA200
        for (int d = 0; d < confirmDays; d++)
        {
            int idx = currentIndex - d;
            if (idx < 0 || sma200[idx] <= 0 || closes[idx] <= sma200[idx])
                return false;
        }

        // N일 전부터 10일 이내에 SMA200 아래였던 날이 있어야 함 (크로스오버 이벤트)
        int lookbackStart = confirmDays;
        int lookbackEnd = Math.Min(confirmDays + 10, currentIndex);
        for (int d = lookbackStart; d <= lookbackEnd; d++)
        {
            int idx = currentIndex - d;
            if (idx >= 0 && sma200[idx] > 0 && closes[idx] < sma200[idx])
                return true;
        }

        return false;
    }

    /// <summary>
    /// 밴드 진입: 상승추세 중 과열 구간에서 집중투자 구간으로 되돌아온 눌림목.
    /// 최근 20일 이내에 과열선 위에 있다가 현재 집중투자 구간으로 내려온 상태.
    /// </summary>
    private bool IsBandEntry(decimal[] closes, decimal[] sma200, decimal[] ema50, int currentIndex)
    {
        // 현재는 집중투자 구간 (SMA200 < close <= overheatLine) — 이미 상위에서 검증됨
        // 최근 20일 이내에 과열 구간이었던 날이 있어야 함
        int lookback = Math.Min(20, currentIndex);

        for (int d = 1; d <= lookback; d++)
        {
            int idx = currentIndex - d;
            if (idx < 0 || sma200[idx] <= 0) break;

            var overheatAtIdx = sma200[idx] * (1 + _config.OverheatPercent);

            // 과열 구간에 있었던 날 발견
            if (closes[idx] > overheatAtIdx)
                return true;

            // SMA200 아래로 간 적이 있으면 이건 밴드가 아님 (새 사이클)
            if (closes[idx] < sma200[idx])
                return false;
        }

        return false;
    }

    /// <summary>
    /// 거래량이 N일 평균 이상인지 확인.
    /// </summary>
    private bool HasVolumeConfirmation(OhlcvBar[] bars, int currentIndex)
    {
        if (_config.VolumeAvgPeriod <= 0 || _config.MinVolumeRatio <= 0)
            return true;

        int period = _config.VolumeAvgPeriod;
        if (currentIndex < period)
            return true;

        decimal avgVolume = 0;
        for (int d = 1; d <= period; d++)
            avgVolume += bars[currentIndex - d].Volume;
        avgVolume /= period;

        if (avgVolume <= 0) return true;

        return bars[currentIndex].Volume >= avgVolume * _config.MinVolumeRatio;
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// TQQQ 200-SMA 변형매매법 (fmkorea "200일 티큐 변형매매법" 기반).
///
/// 핵심 규칙:
///   1. 이격도(Close/SMA200) >= 101% -> 풀매수
///   2. SPY가 200일선 대비 97.75% 이하 -> 진입 차단 (전량 청산 시그널)
///   3. 20일 수익률 표본표준편차 >= 5.9% -> 진입 차단 (변동성 과다)
///   4. 과열 감량: 이격도 139%~146% -> 신뢰도 감소, 146% 이상 -> 진입 차단
///   5. 손절: 진입가 -5.9% 또는 SMA200*0.99 중 높은 쪽
///   6. 목표가: SMA200*1.50 (넓게 설정, TradeSimulator 동적 스탑에 위임)
///
/// 기존 구현 대비 변경점:
///   - EMA50 골든크로스 필터 제거 (이격도 기반으로 단순화)
///   - ATR 적응형 손절 -> 고정 -5.9% 손절
///   - 크로스오버 감지 -> 이격도 >= 101% 어디서든 진입
///   - 과열 105% 차단 -> 139%까지 홀딩, 146%에서 차단
///   - SPY 이격도 필터 추가
///   - 20일 변동성 필터 추가
/// </summary>
public class Tqqq200SmaDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly Tqqq200SmaConfig _config;
    private readonly ISettingsRepository _settingsRepo;

    public PatternType PatternType => PatternType.Tqqq200Sma;

    public Tqqq200SmaDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings,
        ISettingsRepository settingsRepo)
    {
        _indicators = indicators;
        _config = settings.Value.Tqqq200Sma;
        _settingsRepo = settingsRepo;
    }

    public async Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // ── 1. 심볼 체크: TQQQ 전용 (사용자 DB 설정 우선, 없으면 appsettings.json 기본값) ──
        var userSettings = await _settingsRepo.GetAsync(ct);
        var allowed = !string.IsNullOrWhiteSpace(userSettings.Tqqq200SmaAllowedSymbols)
            ? userSettings.Tqqq200SmaAllowedSymbols
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
            : _config.AllowedSymbols;

        if (allowed.Count > 0 && !allowed.Any(s => s.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            return null;

        // ── 2. 바 수 체크: SMA200 계산 + 변동성 계산(20일)에 충분한 데이터 필요 ──
        var minBars = _config.SmaPeriod + 25; // SMA200 + 여유분
        if (bars.Length < minBars)
            return null;

        var closes = ExtractCloses(bars);
        var i = bars.Length - 1;
        var curr = bars[i];

        if (curr.Close <= 0 || curr.Open <= 0)
            return null;

        // ── 3. 지표 계산: SMA200, ATR ──
        var sma200 = _indicators.SMA(closes, _config.SmaPeriod);
        var atr = _indicators.ATR(bars);

        if (sma200[i] <= 0 || atr[i] <= 0)
            return null;

        var smaValue = sma200[i];
        var currentAtr = atr[i];

        // ── 4. 이격도 계산: distance = Close / SMA200 ──
        var distance = curr.Close / smaValue;

        // ── 5. SPY 필터: SPY 이격도 < 97.75% -> 진입 차단 ──
        decimal spyDistance = 0m;
        if (regime != null && regime.Spy200Ma > 0)
        {
            spyDistance = regime.SpyPrice / regime.Spy200Ma;
            if (spyDistance < _config.SpyExitDistancePercent)
                return null;
        }

        // ── 6. 변동성 필터: 20일 일간 수익률 표본표준편차 >= 5.9% -> 진입 차단 ──
        var volatility = CalculateVolatility20d(bars);
        if (volatility >= _config.MaxVolatility20d)
            return null;

        // ── 7. 진입 조건: 이격도 >= 101% (EntryDistancePercent) ──
        if (distance < _config.EntryDistancePercent)
            return null;

        // ── 과열 2단계(146%) 이상: 진입 차단 ──
        if (distance >= _config.OverheatStage2)
            return null;

        // ── 과열 단계별 신뢰도 조정 ──
        decimal confidence;
        if (distance >= _config.OverheatStage1)
        {
            // 과열 1단계 (139%~146%): 진입은 허용하되 낮은 신뢰도
            confidence = 0.45m;
        }
        else
        {
            // 정상 구간 (101%~139%): 표준 신뢰도
            confidence = 0.70m;
        }

        // ── 거래량 확인 (보조 필터) ──
        if (!HasVolumeConfirmation(bars, i))
        {
            confidence -= 0.15m;
            if (confidence < 0.30m)
                return null;
        }

        // ── 8. 손절가: Max(진입가 * (1 - 5.9%), SMA200 * 0.99) ──
        var fixedStop = curr.Close * (1m - _config.FixedStopPercent);
        var smaStop = smaValue * 0.99m;
        var stopLoss = Math.Max(fixedStop, smaStop);

        // ── 9. 목표가: SMA200 * 1.50 (넓게 설정 — TradeSimulator가 동적 스탑으로 관리) ──
        var target = smaValue * 1.50m;

        // 목표가가 진입가보다 낮으면 보정 (이격도가 이미 높을 때)
        if (target <= curr.Close)
            target = curr.Close * 1.10m;

        confidence = Math.Round(Math.Min(1.0m, Math.Max(0.30m, confidence)), 2);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.Tqqq200Sma,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = Math.Round(stopLoss, 2),
            TargetPrice = Math.Round(target, 2),
            Confidence = confidence,
            Details = $"[이격도진입] SMA200=${smaValue:F2}, 이격도={distance:P1}, " +
                      $"SPY이격도={spyDistance:P1}, 변동성20d={volatility:P2}, ATR=${currentAtr:F2}",
            IsActive = true
        };

        return signal;
    }

    /// <summary>
    /// 20일 일간 수익률의 표본표준편차 계산 (ddof=1).
    /// fmkorea 전략의 변동성 필터에 사용.
    /// </summary>
    private decimal CalculateVolatility20d(OhlcvBar[] bars)
    {
        const int period = 20;
        if (bars.Length < period + 1)
            return decimal.MaxValue; // 데이터 부족 시 높은 값 반환 -> 필터에 걸림

        var returns = new decimal[period];
        for (int d = 0; d < period; d++)
        {
            int idx = bars.Length - 1 - d;
            int prev = idx - 1;
            if (prev >= 0 && bars[prev].Close > 0)
                returns[d] = (bars[idx].Close - bars[prev].Close) / bars[prev].Close;
            else
                returns[d] = 0m;
        }

        // 표본표준편차 (ddof=1)
        var mean = returns.Average();
        var sumSquaredDiff = returns.Select(r => (r - mean) * (r - mean)).Sum();
        var variance = sumSquaredDiff / (returns.Length - 1);
        var volatility = (decimal)Math.Sqrt((double)variance);

        return volatility;
    }

    /// <summary>
    /// 거래량이 N일 평균 이상인지 확인 (보조 필터).
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

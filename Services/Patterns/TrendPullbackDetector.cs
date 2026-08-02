using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

public class TrendPullbackDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly TrendPullbackConfig _config;

    public PatternType PatternType => PatternType.TrendPullback;

    public TrendPullbackDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.TrendPullback;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 full implementation
        if (bars.Length < _config.MaPeriod + _config.TrendConfirmationDays)
            return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var sma = _indicators.SMA(closes, _config.MaPeriod);

        var curr = bars[^1];
        var currentSma = sma[^1];
        if (currentSma == 0) return Task.FromResult<PatternSignal?>(null);

        var trendUp = true;
        for (int i = bars.Length - _config.TrendConfirmationDays; i < bars.Length - 1; i++)
        {
            // Guard i-1 >= 0 and both SMA values must be valid (non-zero)
            if (i - 1 < 0 || sma[i] == 0 || sma[i - 1] == 0 || sma[i] <= sma[i - 1])
            {
                trendUp = false;
                break;
            }
        }
        if (!trendUp) return Task.FromResult<PatternSignal?>(null);

        // pullback > 0: price is below SMA (positive pullback)
        // pullback < 0: price is above SMA (negative = price above MA)
        // Allow entry when price is within MaxPullbackFromMa below the MA (pullback in [0, max])
        // OR when price is slightly above the MA (within MaxPullbackFromMa range above it)
        // This means: abs(pullback) <= MaxPullbackFromMa — entry allowed whether above or below MA
        var pullback = (currentSma - curr.Close) / currentSma;
        if (Math.Abs(pullback) > _config.MaxPullbackFromMa)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.TrendPullback,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Close - atr[^1] * _config.AtrStopMultiplier,
            TargetPrice = currentSma + atr[^1] * _config.AtrTargetMultiplier,
            // pullback > 0 = SMA 아래(이상적 진입), < 0 = SMA 위(유효하지만 약한 진입)
            // 어느 방향이든 SMA에 가까울수록 진입 적합 → 1 - (거리 비율)
            Confidence = Math.Clamp(1.0m - Math.Abs(pullback) / _config.MaxPullbackFromMa, 0.1m, 1.0m),
            Details = $"20MA Pullback: {pullback:P1}, SMA: {currentSma:F2}",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

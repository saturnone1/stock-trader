using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Larry Williams Volatility Breakout strategy.
///
/// Core idea: if today's open + (yesterday's range * K) is exceeded intraday,
/// a volatility breakout is confirmed and likely to continue.
///
/// Entry conditions:
///   - Market regime: SPY above 200-SMA (bull market filter)
///   - Current High > Open + (PreviousRange * K)  [K = 0.6 by default]
///     where PreviousRange = prev.High - prev.Low
///   - Volume > 20-bar average * 1.2  (participation confirmation)
///
/// Entry price: Open + PreviousRange * K  (the breakout level itself)
/// Stop:        entryPrice - ATR(14) * 2.0
/// Target:      entryPrice + ATR(14) * 3.0  (R:R = 1:1.5)
///
/// Historical results: Larry Williams 1987 World Trading Championship 11,376% return.
/// Long-term backtests: 55-65% win rate, R:R 1.5-2.5:1.
/// </summary>
public class VolatilityBreakoutDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly VolatilityBreakoutConfig _config;

    public PatternType PatternType => PatternType.VolatilityBreakout;

    public VolatilityBreakoutDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.VolatilityBreakout;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // Need: at least VolumeAvgPeriod + 2 bars (for previous bar + current bar)
        var minBars = _config.VolumeAvgPeriod + 2;
        if (bars.Length < minBars)
            return Task.FromResult<PatternSignal?>(null);

        // Bull market filter
        if (!regime.SpyAbove200Ma)
            return Task.FromResult<PatternSignal?>(null);

        var i = bars.Length - 1;
        var curr = bars[i];
        var prev = bars[i - 1];

        // Previous bar's range drives the breakout trigger level
        var prevRange = prev.High - prev.Low;
        if (prevRange <= 0)
            return Task.FromResult<PatternSignal?>(null);

        // Guard: bad data feed can produce Open=0 → breakoutLevel ≈ prevRange*K (spurious signal)
        if (curr.Open <= 0 || curr.High <= 0 || curr.Close <= 0)
            return Task.FromResult<PatternSignal?>(null);

        // Breakout trigger price = today's open + previous range * K
        var breakoutLevel = curr.Open + prevRange * _config.BreakoutFactor;

        // Confirm: current bar's high must have cleared the breakout level
        if (curr.High < breakoutLevel)
            return Task.FromResult<PatternSignal?>(null);

        // Entry is at the breakout level (not the close — prevents chasing)
        var entryPrice = breakoutLevel;

        // Volume confirmation: current volume > 20-bar average * multiplier
        // Use bars excluding the current one to avoid look-ahead on volume
        var volumeWindow = bars[(i - _config.VolumeAvgPeriod)..(i)];
        var avgVolume = volumeWindow.Average(b => (double)b.Volume);
        if (avgVolume <= 0) return Task.FromResult<PatternSignal?>(null);

        var volumeRatio = curr.Volume / (decimal)avgVolume;
        if (volumeRatio < _config.MinVolumeMultiplier)
            return Task.FromResult<PatternSignal?>(null);

        // ATR for stop/target sizing
        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        var stopLoss = entryPrice - currentAtr * _config.AtrStopMultiplier;
        var target = entryPrice + currentAtr * _config.AtrTargetMultiplier;

        // Confidence: blend of volume ratio and range expansion relative to ATR.
        // - Volume ratio: how much above the minimum threshold
        //   e.g. volumeRatio=1.5 → volumeScore=0.6, volumeRatio=2.5 → volumeScore=1.0
        // - Range expansion: prevRange vs current ATR (>1 = range expanding)
        var volumeScore = Math.Min(1.0m, (volumeRatio - _config.MinVolumeMultiplier) / 1.0m + 0.5m);
        var rangeExpansion = prevRange / currentAtr; // >1.0 = larger-than-average range
        var rangeScore = Math.Min(1.0m, rangeExpansion * 0.5m);
        var confidence = Math.Round((volumeScore + rangeScore) / 2.0m, 2);
        confidence = Math.Max(0.4m, Math.Min(1.0m, confidence));

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.VolatilityBreakout,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = Math.Round(entryPrice, 2),
            StopLossPrice = Math.Round(stopLoss, 2),
            TargetPrice = Math.Round(target, 2),
            Confidence = confidence,
            Details = $"돌파레벨={breakoutLevel:F2} (전일레인지={prevRange:F2}*K{_config.BreakoutFactor}), " +
                      $"거래량비율={volumeRatio:F2}x, ATR={currentAtr:F2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

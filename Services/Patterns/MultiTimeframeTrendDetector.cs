using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Multi-timeframe trend strategy.
/// Long-term: Price above SMA(50) for N bars confirms uptrend.
/// Entry: Price pulls back to SMA(20) zone (within MaxPullbackPercent) and bounces.
/// Combines macro trend confirmation with micro pullback entry.
/// </summary>
public class MultiTimeframeTrendDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly MultiTimeframeTrendConfig _config;

    public PatternType PatternType => PatternType.MultiTimeframeTrend;

    public MultiTimeframeTrendDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.MultiTimeframeTrend;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < _config.LongTrendMaPeriod + _config.TrendConfirmationBars)
            return Task.FromResult<PatternSignal?>(null);

        if (!regime.SpyAbove200Ma)
            return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var longMa = _indicators.SMA(closes, _config.LongTrendMaPeriod);
        var shortMa = _indicators.SMA(closes, _config.ShortEntryMaPeriod);

        var i = bars.Length - 1;
        var curr = bars[i];

        // Confirm uptrend: price above long MA for N consecutive bars
        for (int j = i - _config.TrendConfirmationBars; j < i; j++)
        {
            if (j < 0 || longMa[j] <= 0 || closes[j] <= longMa[j])
                return Task.FromResult<PatternSignal?>(null);
        }

        // Current price must still be above long MA
        if (curr.Close <= longMa[i] || longMa[i] <= 0)
            return Task.FromResult<PatternSignal?>(null);

        // Pullback condition: price near short MA
        if (shortMa[i] <= 0) return Task.FromResult<PatternSignal?>(null);

        var distanceFromShortMa = (curr.Close - shortMa[i]) / shortMa[i];

        // Pullback entry: price must be at or below short MA (not above it).
        // Allow a tiny tolerance of 0.1% above to catch exact touches on the MA,
        // but reject anything clearly above (that is trend-following, not pullback).
        // Lower bound: reject if price has fallen more than MaxPullbackPercent below MA.
        if (distanceFromShortMa > _config.MaxDistanceAboveShortMa || distanceFromShortMa < -_config.MaxPullbackPercent)
            return Task.FromResult<PatternSignal?>(null);

        // Bounce confirmation: current bar is bullish
        if (!curr.IsBullish)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        var stopLoss = Math.Min(shortMa[i], curr.Close - currentAtr * _config.AtrStopMultiplier);
        var target = curr.Close + currentAtr * _config.AtrTargetMultiplier;

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.MultiTimeframeTrend,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = 0.65m,
            Details = $"SMA({_config.LongTrendMaPeriod}) 상승추세 + SMA({_config.ShortEntryMaPeriod}) 풀백 진입, 거리={distanceFromShortMa:P2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Keltner Channel mean reversion strategy.
/// Entry: Price closes below lower Keltner band + RSI oversold confirmation.
/// Target: Middle band (EMA). Stop: Below recent low - 1 ATR.
/// </summary>
public class MeanReversionChannelDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly MeanReversionChannelConfig _config;

    public PatternType PatternType => PatternType.MeanReversionChannel;

    public MeanReversionChannelDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.MeanReversionChannel;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < _config.EmaPeriod + 10)
            return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var (upper, middle, lower) = _indicators.KeltnerChannel(
            bars, _config.EmaPeriod, _config.AtrPeriod, _config.AtrMultiplier);
        var rsi = _indicators.RSI(closes, _config.RsiPeriod);

        var i = bars.Length - 1;
        var curr = bars[i];

        // Price must be at or below lower Keltner band
        if (lower[i] <= 0 || curr.Close > lower[i])
            return Task.FromResult<PatternSignal?>(null);

        // RSI oversold confirmation
        if (rsi[i] <= 0 || rsi[i] > _config.RsiOversold)
            return Task.FromResult<PatternSignal?>(null);

        // Only trade in bull regime (mean reversion works better in uptrends)
        if (!regime.SpyAbove200Ma)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        // Find recent low for stop placement
        var lookback = Math.Min(_config.RecentLowLookbackBars, bars.Length);
        var recentLow = bars[^lookback..].Min(b => b.Low);

        var stopLoss = recentLow - currentAtr;
        var target = middle[i]; // Mean reversion target = middle band

        // Ensure target > entry (meaningful trade)
        if (target <= curr.Close)
            return Task.FromResult<PatternSignal?>(null);

        var confidence = Math.Min(1.0m, (lower[i] - curr.Close) / currentAtr * 0.5m + 0.5m);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.MeanReversionChannel,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = confidence,
            Details = $"Keltner 하단 이탈, RSI={rsi[i]:F1}, 목표=EMA({_config.EmaPeriod})={middle[i]:F2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

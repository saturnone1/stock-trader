using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class BreakoutDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly BreakoutConfig _config;

    public PatternType PatternType => PatternType.Breakout;

    public BreakoutDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.Breakout;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < _config.LookbackDays) return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var lookbackBars = bars[^_config.LookbackDays..];
        var high52w = lookbackBars.Max(b => b.High);
        var curr = bars[^1];

        var avgVolume = lookbackBars.Average(b => (decimal)b.Volume);
        var volumeRatio = curr.Volume / avgVolume;

        if (curr.Close <= high52w * (1 + _config.BreakoutMarginPercent))
            return Task.FromResult<PatternSignal?>(null);

        if (volumeRatio < _config.MinVolumeMultiplier)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[^1];
        var stopLoss = curr.Close - currentAtr * 2;
        var target = curr.Close + currentAtr * 3;

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.Breakout,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = Math.Min(1.0m, volumeRatio / 3),
            Details = $"52W High Break, Volume: {volumeRatio:F1}x avg",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

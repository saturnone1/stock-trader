using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class RsiMeanReversionDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly RsiMeanReversionConfig _config;

    public PatternType PatternType => PatternType.RsiMeanReversion;

    public RsiMeanReversionDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.RsiMeanReversion;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 implementation
        if (bars.Length < _config.Period + 1) return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var closes = bars.Select(b => b.Close).ToArray();
        var rsi = _indicators.RSI(closes, _config.Period);
        var curr = bars[^1];
        var currentRsi = rsi[^1];

        if (currentRsi >= _config.OversoldThreshold || currentRsi == 0)
            return Task.FromResult<PatternSignal?>(null);

        var prevBar = bars[^2];
        var volumeIncrease = curr.Volume > prevBar.Volume * 1.2m;
        if (!volumeIncrease) return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.RsiMeanReversion,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Close - atr[^1] * 1.5m,
            TargetPrice = curr.Close + atr[^1] * 2m,
            Confidence = Math.Min(1.0m, (_config.OversoldThreshold - currentRsi) / 30m),
            Details = $"RSI: {currentRsi:F1}, Volume increase: {(decimal)curr.Volume / prevBar.Volume:F1}x",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

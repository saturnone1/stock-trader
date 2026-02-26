using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class VolatilityExpansionDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly VolatilityExpansionConfig _config;

    public PatternType PatternType => PatternType.VolatilityExpansion;

    public VolatilityExpansionDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.VolatilityExpansion;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 implementation
        if (bars.Length < _config.BollingerPeriod + 1)
            return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var closes = bars.Select(b => b.Close).ToArray();
        var (upper, middle, lower) = _indicators.BollingerBands(
            closes, _config.BollingerPeriod, _config.StdDevMultiplier);

        var curr = bars[^1];
        if (curr.Close <= upper[^1]) return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.VolatilityExpansion,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = middle[^1],
            TargetPrice = curr.Close + (curr.Close - middle[^1]),
            Confidence = Math.Min(1.0m, (curr.Close - upper[^1]) / atr[^1]),
            Details = $"BB Upper Break, Close: {curr.Close:F2}, Upper: {upper[^1]:F2}",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

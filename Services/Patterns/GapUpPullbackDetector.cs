using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class GapUpPullbackDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly GapUpPullbackConfig _config;

    public PatternType PatternType => PatternType.GapUpPullback;

    public GapUpPullbackDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.GapUpPullback;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < 2) return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var prev = bars[^2];
        var curr = bars[^1];

        var gapPercent = (curr.Open - prev.Close) / prev.Close;
        if (gapPercent < _config.MinGapPercent) return Task.FromResult<PatternSignal?>(null);

        if (curr.Volume < _config.MinVolume) return Task.FromResult<PatternSignal?>(null);

        var pullbackFromHigh = curr.High > curr.Open
            ? (curr.High - curr.Close) / (curr.High - curr.Open)
            : 0;
        if (pullbackFromHigh < _config.MaxPullbackPercent) return Task.FromResult<PatternSignal?>(null);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.GapUpPullback,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Low,
            TargetPrice = curr.Close + (curr.Close - curr.Low) * 2,
            Confidence = Math.Min(1.0m, gapPercent * 10),
            Details = $"Gap: {gapPercent:P1}, Pullback: {pullbackFromHigh:P1}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

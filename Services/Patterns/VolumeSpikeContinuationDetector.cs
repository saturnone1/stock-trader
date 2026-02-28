using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class VolumeSpikeContinuationDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly VolumeSpikeConfig _config;

    public PatternType PatternType => PatternType.VolumeSpikeContinuation;

    public VolumeSpikeContinuationDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.VolumeSpikeContinuation;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 implementation
        // VolumeAvgPeriod + 1 for the slice, ContinuationBars for the loop — take the max.
        var minBars = Math.Max(_config.VolumeAvgPeriod + 1, _config.ContinuationBars);
        if (bars.Length < minBars) return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var avgVolume = bars[^(_config.VolumeAvgPeriod + 1)..^1].Average(b => (decimal)b.Volume);
        var curr = bars[^1];
        var volumeRatio = curr.Volume / avgVolume;

        if (volumeRatio < _config.VolumeMultiplier || !curr.IsBullish)
            return Task.FromResult<PatternSignal?>(null);

        var continuationCount = 0;
        for (int i = bars.Length - _config.ContinuationBars; i < bars.Length; i++)
        {
            if (bars[i].IsBullish) continuationCount++;
        }
        if (continuationCount < _config.ContinuationBars)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.VolumeSpikeContinuation,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Close - atr[^1] * _config.AtrStopMultiplier,
            TargetPrice = curr.Close + atr[^1] * _config.AtrTargetMultiplier,
            Confidence = Math.Min(1.0m, volumeRatio / 5m),
            Details = $"Volume: {volumeRatio:F1}x avg, {continuationCount} bullish bars",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

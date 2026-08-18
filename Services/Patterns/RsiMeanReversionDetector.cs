using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

public class RsiMeanReversionDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly RsiMeanReversionConfig _config;

    public PatternType PatternType => PatternType.RsiMeanReversion;

    public RsiMeanReversionDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.RsiMeanReversion;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 implementation
        if (bars.Length < _config.Period + 1) return Task.FromResult<PatternSignal?>(null);
        // Regime filter removed: mean reversion works in all market regimes (bear markets too)

        var closes = ExtractCloses(bars);
        var rsi = _indicators.RSI(closes, _config.Period);
        var curr = bars[^1];
        var currentRsi = rsi[^1];

        if (currentRsi >= _config.OversoldThreshold || currentRsi == 0)
            return Task.FromResult<PatternSignal?>(null);

        // Volume filter: MinVolumeIncreaseMultiplier=1.0 effectively disables this check
        // (oversold bounces often occur on declining volume)
        var prevBar = bars[^2];
        var volumeIncrease = curr.Volume > prevBar.Volume * _config.MinVolumeIncreaseMultiplier;
        if (!volumeIncrease) return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.RsiMeanReversion,
            DetectedAt = curr.Timestamp,
            SignalBarAt = curr.Timestamp,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Close - atr[^1] * _config.AtrStopMultiplier,
            TargetPrice = curr.Close + atr[^1] * _config.AtrTargetMultiplier,
            Confidence = Math.Min(1.0m, (_config.OversoldThreshold - currentRsi) / 30m),
            Details = $"RSI: {currentRsi:F1}, Volume increase: {(decimal)curr.Volume / prevBar.Volume:F1}x",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

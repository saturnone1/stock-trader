using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Domain.MarketData;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class VwapReversionDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly VwapReversionConfig _config;

    public PatternType PatternType => PatternType.VwapReversion;

    public VwapReversionDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.VwapReversion;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < 10) return Task.FromResult<PatternSignal?>(null);
        if (!TimeFrameCatalog.IsIntraday(bars[^1].TimeFrame))
            return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var vwap = _indicators.VWAP(bars);
        var curr = bars[^1];
        var currentVwap = vwap[^1];

        if (currentVwap == 0) return Task.FromResult<PatternSignal?>(null);

        var deviation = (curr.Close - currentVwap) / currentVwap;

        if (deviation >= -_config.MaxDeviationPercent)
            return Task.FromResult<PatternSignal?>(null);

        var bouncing = curr.Close > curr.Low + (curr.High - curr.Low) * _config.MinBounceFromLowPercent;
        if (!bouncing) return Task.FromResult<PatternSignal?>(null);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.VwapReversion,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Low,
            TargetPrice = currentVwap,
            Confidence = Math.Min(1.0m, Math.Abs(deviation) * 20),
            Details = $"VWAP Deviation: {deviation:P2}, VWAP: {currentVwap:F2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

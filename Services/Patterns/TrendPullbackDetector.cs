using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

public class TrendPullbackDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly TrendPullbackConfig _config;

    public PatternType PatternType => PatternType.TrendPullback;

    public TrendPullbackDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.TrendPullback;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 full implementation
        if (bars.Length < _config.MaPeriod + _config.TrendConfirmationDays)
            return Task.FromResult<PatternSignal?>(null);
        if (!regime.SpyAbove200Ma) return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var sma = _indicators.SMA(closes, _config.MaPeriod);

        var curr = bars[^1];
        var currentSma = sma[^1];
        if (currentSma == 0) return Task.FromResult<PatternSignal?>(null);

        var trendUp = true;
        for (int i = bars.Length - _config.TrendConfirmationDays; i < bars.Length - 1; i++)
        {
            // Guard i-1 >= 0 and both SMA values must be valid (non-zero)
            if (i - 1 < 0 || sma[i] == 0 || sma[i - 1] == 0 || sma[i] <= sma[i - 1])
            {
                trendUp = false;
                break;
            }
        }
        if (!trendUp) return Task.FromResult<PatternSignal?>(null);

        var pullback = (currentSma - curr.Close) / currentSma;
        if (pullback < 0 || pullback > _config.MaxPullbackFromMa)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.TrendPullback,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = curr.Close - atr[^1] * _config.AtrStopMultiplier,
            TargetPrice = currentSma + atr[^1] * _config.AtrTargetMultiplier,
            Confidence = Math.Min(1.0m, pullback / _config.MaxPullbackFromMa),
            Details = $"20MA Pullback: {pullback:P1}, SMA: {currentSma:F2}",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}

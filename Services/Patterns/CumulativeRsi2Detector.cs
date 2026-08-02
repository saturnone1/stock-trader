using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Connors cumulative RSI(2) mean reversion strategy.
///
/// Entry:
///   - Stock is above long-term trend MA (default 200-SMA)
///   - Cumulative RSI(2) over the recent 2 bars is <= 10
///
/// Exit:
///   - Cumulative RSI(2) >= 65, or
///   - Close falls below long-term trend MA
///
/// The detector emits a wide placeholder target for live broker brackets.
/// Backtest/live exit managers use the indicator-based exit rules above.
/// </summary>
public sealed class CumulativeRsi2Detector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly CumulativeRsi2Config _config;

    public PatternType PatternType => PatternType.CumulativeRsi2;

    public CumulativeRsi2Detector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.CumulativeRsi2;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        var minBars = Math.Max(_config.LongTrendMaPeriod, _config.ExitSmaPeriod)
            + _config.CumulativePeriod + _config.RsiPeriod + 2;
        if (bars.Length < minBars)
            return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var i = bars.Length - 1;
        var curr = bars[i];

        var longTrendMa = _indicators.SMA(closes, _config.LongTrendMaPeriod);
        if (longTrendMa[i] <= 0 || curr.Close <= longTrendMa[i])
            return Task.FromResult<PatternSignal?>(null);

        var cumulativeRsi = _indicators.CumulativeRsi(
            closes, _config.RsiPeriod, _config.CumulativePeriod);
        var currentCumulativeRsi = cumulativeRsi[i];
        if (currentCumulativeRsi <= 0 || currentCumulativeRsi > _config.EntryThreshold)
            return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0)
            return Task.FromResult<PatternSignal?>(null);

        var stopLoss = curr.Close - currentAtr * _config.AtrStopMultiplier;
        if (stopLoss <= 0 || stopLoss >= curr.Close)
            return Task.FromResult<PatternSignal?>(null);

        var exitSma = _indicators.SMA(closes, _config.ExitSmaPeriod);
        var placeholderTarget = Math.Max(
            exitSma[i],
            curr.Close + currentAtr * _config.PlaceholderTargetAtrMultiplier);
        if (placeholderTarget <= curr.Close)
            return Task.FromResult<PatternSignal?>(null);

        var thresholdRange = Math.Max(1m, _config.EntryThreshold);
        var oversoldRatio = Math.Clamp((_config.EntryThreshold - currentCumulativeRsi) / thresholdRange, 0m, 1m);
        var confidence = Math.Round(Math.Min(1.0m, 0.55m + oversoldRatio * 0.45m), 2);

        return Task.FromResult<PatternSignal?>(new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.CumulativeRsi2,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = Math.Round(stopLoss, 2),
            TargetPrice = Math.Round(placeholderTarget, 2),
            Confidence = confidence,
            Details = $"CumRSI({_config.RsiPeriod},{_config.CumulativePeriod})={currentCumulativeRsi:F1} " +
                      $"(진입<={_config.EntryThreshold}, 청산>={_config.ExitThreshold}), " +
                      $"{_config.LongTrendMaPeriod}SMA={longTrendMa[i]:F2}, ATR={currentAtr:F2}",
            IsActive = true
        });
    }
}

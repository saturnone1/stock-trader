using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Connors Research RSI(2) + Bollinger Band mean reversion strategy.
///
/// Logic:
///   - Market regime: SPY above 200-SMA (bull market filter)
///   - Stock is in long-term uptrend: Close > 200-SMA
///   - RSI(2) &lt; 10: extreme short-term oversold condition
///   - Close &lt; lower Bollinger Band (20, 2.0): price compressed below statistical floor
///
/// Entry: current Close (next bar open in live trading)
/// Stop:  entry - ATR(14) * 1.5
/// Target: middle Bollinger Band (20-SMA) — mean reversion back to equilibrium
///
/// Backtested results (Connors Research, S&P 500 1995-2007): 75-82% win rate.
/// </summary>
public class Rsi2BollingerDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly Rsi2BollingerConfig _config;

    public PatternType PatternType => PatternType.Rsi2Bollinger;

    public Rsi2BollingerDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.Rsi2Bollinger;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // Need enough bars for 200-SMA + Bollinger(20) warmup.
        // RSI(2) only needs 3+ bars but 200-SMA dominates the minimum.
        var minBars = Math.Max(_config.LongTrendMaPeriod, _config.BollingerPeriod) + 5;
        if (bars.Length < minBars)
            return Task.FromResult<PatternSignal?>(null);

        // Regime filter removed: RSI(2)+Bollinger mean reversion is a pure oversold bounce strategy
        // that works in all market conditions (Connors Research backtests include both bull and bear)

        var closes = ExtractCloses(bars);
        var i = bars.Length - 1;
        var curr = bars[i];

        // Long-term uptrend filter: stock must be above its own 200-SMA
        var longTrendMa = _indicators.SMA(closes, _config.LongTrendMaPeriod);
        if (longTrendMa[i] <= 0 || curr.Close <= longTrendMa[i])
            return Task.FromResult<PatternSignal?>(null);

        // RSI(2) oversold: below threshold (default 10)
        var rsi2 = _indicators.RSI(closes, _config.RsiPeriod);
        // Skip if RSI warmup incomplete (index < period → RSI = 0 placeholder, not real oversold)
        if (i < _config.RsiPeriod || rsi2[i] >= _config.RsiThreshold)
            return Task.FromResult<PatternSignal?>(null);

        // Price below lower Bollinger Band
        var (_, bbMiddle, bbLower) = _indicators.BollingerBands(
            closes, _config.BollingerPeriod, _config.BollingerStdDev);

        if (bbLower[i] <= 0 || curr.Close > bbLower[i])
            return Task.FromResult<PatternSignal?>(null);

        // Target is mean reversion back to middle band (20-SMA)
        var target = bbMiddle[i];
        if (target <= curr.Close)
            return Task.FromResult<PatternSignal?>(null);

        // ATR for stop placement
        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        var stopLoss = curr.Close - currentAtr * _config.AtrStopMultiplier;

        // Confidence: the more oversold RSI(2) is, the higher the confidence.
        // RSI(2) range [0, threshold] → confidence [0.5, 1.0]
        // e.g. RSI(2)=0 → 1.0, RSI(2)=10 → 0.5
        var rsiRatio = _config.RsiThreshold > 0
            ? rsi2[i] / _config.RsiThreshold  // 0 = maximally oversold, 1 = at threshold
            : 0m;
        var confidence = Math.Round(1.0m - rsiRatio * 0.5m, 2);
        confidence = Math.Max(0.5m, Math.Min(1.0m, confidence));

        // Extra boost if price is significantly below lower band
        var bandDrop = (bbLower[i] - curr.Close) / currentAtr;
        confidence = Math.Min(1.0m, confidence + bandDrop * 0.05m);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.Rsi2Bollinger,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = Math.Round(stopLoss, 2),
            TargetPrice = Math.Round(target, 2),
            Confidence = Math.Round(confidence, 2),
            Details = $"RSI({_config.RsiPeriod})={rsi2[i]:F1} (임계값 {_config.RsiThreshold}), " +
                      $"BB하단={bbLower[i]:F2}, BB중간={bbMiddle[i]:F2}, " +
                      $"200SMA={longTrendMa[i]:F2}, ATR={currentAtr:F2}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

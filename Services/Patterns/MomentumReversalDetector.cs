using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Momentum/Reversal composite strategy.
/// Momentum entry: Fast EMA crosses above Slow EMA + MACD histogram positive + RSI 50-70.
/// Reversal entry: RSI oversold + MACD histogram turns positive from negative.
/// </summary>
public class MomentumReversalDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly MomentumReversalConfig _config;

    public PatternType PatternType => PatternType.MomentumReversal;

    public MomentumReversalDetector(IIndicatorService indicators, IOptionsSnapshot<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.MomentumReversal;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < _config.SlowEmaPeriod + 10)
            return Task.FromResult<PatternSignal?>(null);

        var closes = ExtractCloses(bars);
        var rsi = _indicators.RSI(closes, _config.RsiPeriod);
        var (macdLine, signalLine, histogram) = _indicators.MACD(
            closes, _config.FastEmaPeriod, _config.SlowEmaPeriod, _config.MacdSignalPeriod);
        var fastEma = _indicators.EMA(closes, _config.FastEmaPeriod);
        var slowEma = _indicators.EMA(closes, _config.SlowEmaPeriod);

        var curr = bars[^1];
        var i = bars.Length - 1;
        var prevI = bars.Length - 2;

        if (prevI < 0) return Task.FromResult<PatternSignal?>(null);

        string mode;
        decimal confidence;

        // Momentum mode: EMA crossover just happened + MACD positive + RSI in momentum zone
        var emaCrossUp = fastEma[prevI] <= slowEma[prevI] && fastEma[i] > slowEma[i];
        var macdPositive = histogram[i] > 0;
        var rsiMomentum = rsi[i] > _config.RsiMomentumMin && rsi[i] < _config.RsiOverbought;

        // Reversal mode: RSI oversold + MACD histogram turning positive
        var rsiOversold = rsi[i] < _config.RsiOversold;
        var histogramTurnPositive = histogram[prevI] < 0 && histogram[i] >= 0;

        if (emaCrossUp && macdPositive && rsiMomentum)
        {
            mode = "모멘텀";
            confidence = 0.7m;
        }
        else if (rsiOversold && histogramTurnPositive && regime.SpyAbove200Ma)
        {
            mode = "리버설";
            confidence = 0.6m;
        }
        else
        {
            return Task.FromResult<PatternSignal?>(null);
        }

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[i];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        var stopLoss = curr.Close - currentAtr * _config.AtrStopMultiplier;
        var target = curr.Close + currentAtr * _config.AtrTargetMultiplier;

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.MomentumReversal,
            DetectedAt = curr.Timestamp,
            SignalBarAt = curr.Timestamp,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = confidence,
            Details = $"{mode}: RSI={rsi[i]:F1}, MACD Hist={histogram[i]:F4}",
            IsActive = true
        };

        return Task.FromResult<PatternSignal?>(signal);
    }
}

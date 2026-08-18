using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// 한 종목의 한 규칙 평가 주기에서 공유하는 지표 계산 캐시다.
/// 컨텍스트는 종목 간에 재사용하지 않는다.
/// </summary>
internal sealed class RuleIndicatorEvaluationContext
{
    private readonly IIndicatorService _indicators;
    private readonly Dictionary<string, object> _cache = new(StringComparer.Ordinal);

    internal RuleIndicatorEvaluationContext(OhlcvBar[] bars, IIndicatorService indicators)
    {
        Bars = bars;
        _indicators = indicators;
        Closes = IndicatorService.ExtractCloses(bars);
    }

    internal OhlcvBar[] Bars { get; }
    internal decimal[] Closes { get; }

    internal decimal[] GetRsi(int period) =>
        GetOrAdd($"rsi_{period}", () => _indicators.RSI(Closes, period));

    internal decimal[] GetCumulativeRsi(int period, int cumulativePeriod) =>
        GetOrAdd(
            $"cumulative_rsi_{period}_{cumulativePeriod}",
            () => _indicators.CumulativeRsi(Closes, period, cumulativePeriod));

    internal decimal[] GetSma(int period) =>
        GetOrAdd($"sma_{period}", () => _indicators.SMA(Closes, period));

    internal decimal[] GetEma(int period) =>
        GetOrAdd($"ema_{period}", () => _indicators.EMA(Closes, period));

    internal decimal[] GetAtr(int period) =>
        GetOrAdd($"atr_{period}", () => _indicators.ATR(Bars, period));

    internal (decimal[] Upper, decimal[] Middle, decimal[] Lower) GetBollinger(
        int period,
        decimal standardDeviation) =>
        GetOrAdd(
            $"bb_{period}_{standardDeviation}",
            () => _indicators.BollingerBands(Closes, period, standardDeviation));

    internal (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) GetMacd(
        int fast,
        int slow,
        int signal) =>
        GetOrAdd(
            $"macd_{fast}_{slow}_{signal}",
            () => _indicators.MACD(Closes, fast, slow, signal));

    internal decimal[] GetObv() =>
        GetOrAdd("obv", () => _indicators.OBV(Bars));

    private T GetOrAdd<T>(string key, Func<T> factory)
    {
        if (_cache.TryGetValue(key, out var cached)) return (T)cached;
        var value = factory();
        _cache[key] = value!;
        return value;
    }
}

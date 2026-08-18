using StockTrader.Models;

namespace StockTrader.Services.Patterns;

internal static class StandardRuleIndicatorCalculators
{
    internal static IReadOnlyDictionary<string, RuleIndicatorCalculator> All { get; } =
        new Dictionary<string, RuleIndicatorCalculator>(StringComparer.OrdinalIgnoreCase)
        {
            ["RSI"] = Rsi,
            ["CUMULATIVE_RSI"] = CumulativeRsi,
            ["PRICE_VS_SMA"] = PriceVsSma,
            ["PRICE_VS_EMA"] = PriceVsEma,
            ["MACD_HIST"] = MacdHistogram,
            ["BOLLINGER_POS"] = BollingerPosition,
            ["VOLUME_RATIO"] = VolumeRatio,
            ["PRICE_CHANGE"] = PriceChange,
            ["ATR"] = Atr,
            ["SMA_SLOPE"] = SmaSlope,
            ["CANDLE_BODY"] = CandleBody,
            ["ATR_PERCENT"] = AtrPercent,
            ["VOLATILITY_20D"] = Volatility
        };

    private static (decimal, decimal) Rsi(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = context.GetRsi(parameters.GetInt("period", 14));
        return (values[currentIndex], values[previousIndex]);
    }

    private static (decimal, decimal) CumulativeRsi(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = context.GetCumulativeRsi(
            parameters.GetInt("period", 2),
            parameters.GetInt("cumulativePeriod", 2));
        return (values[currentIndex], values[previousIndex]);
    }

    private static (decimal, decimal) PriceVsSma(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        PriceVsAverage(
            context.Closes,
            context.GetSma(parameters.GetInt("period", 200)),
            currentIndex,
            previousIndex);

    private static (decimal, decimal) PriceVsEma(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        PriceVsAverage(
            context.Closes,
            context.GetEma(parameters.GetInt("period", 20)),
            currentIndex,
            previousIndex);

    private static (decimal, decimal) PriceVsAverage(
        decimal[] closes,
        decimal[] average,
        int currentIndex,
        int previousIndex)
    {
        if (average[currentIndex] == 0) return (0, 0);
        return (
            (closes[currentIndex] - average[currentIndex]) / average[currentIndex] * 100,
            average[previousIndex] == 0
                ? 0
                : (closes[previousIndex] - average[previousIndex]) / average[previousIndex] * 100);
    }

    private static (decimal, decimal) MacdHistogram(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var (_, _, histogram) = context.GetMacd(
            parameters.GetInt("fast", 12),
            parameters.GetInt("slow", 26),
            parameters.GetInt("signal", 9));
        return (histogram[currentIndex], histogram[previousIndex]);
    }

    private static (decimal, decimal) BollingerPosition(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var (upper, _, lower) = context.GetBollinger(
            parameters.GetInt("period", 20),
            parameters.GetDecimal("stddev", 2m));
        var currentRange = upper[currentIndex] - lower[currentIndex];
        if (currentRange == 0) return (0.5m, 0.5m);
        var previousRange = upper[previousIndex] - lower[previousIndex];
        return (
            (context.Closes[currentIndex] - lower[currentIndex]) / currentRange,
            previousRange == 0
                ? 0.5m
                : (context.Closes[previousIndex] - lower[previousIndex]) / previousRange);
    }

    private static (decimal, decimal) VolumeRatio(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        if (currentIndex < period) return (0, 0);
        var currentAverage = AveragePriorVolume(context.Bars, currentIndex, period);
        if (currentAverage == 0) return (0, 0);
        var previousAverage = previousIndex >= period
            ? AveragePriorVolume(context.Bars, previousIndex, period)
            : 0;
        return (
            context.Bars[currentIndex].Volume / currentAverage,
            previousAverage == 0 ? 0 : context.Bars[previousIndex].Volume / previousAverage);
    }

    private static decimal AveragePriorVolume(OhlcvBar[] bars, int index, int period)
    {
        decimal total = 0;
        for (var i = index - period; i < index; i++) total += bars[i].Volume;
        return total / period;
    }

    private static (decimal, decimal) PriceChange(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var barsBack = parameters.GetInt("bars", 1);
        if (currentIndex < barsBack || previousIndex < barsBack) return (0, 0);
        return (
            PercentChange(context.Closes[currentIndex - barsBack], context.Closes[currentIndex]),
            PercentChange(context.Closes[previousIndex - barsBack], context.Closes[previousIndex]));
    }

    private static decimal PercentChange(decimal from, decimal to) =>
        from == 0 ? 0 : (to - from) / from * 100;

    private static (decimal, decimal) Atr(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = context.GetAtr(parameters.GetInt("period", 14));
        return (values[currentIndex], values[previousIndex]);
    }

    private static (decimal, decimal) SmaSlope(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var lookback = parameters.GetInt("lookback", 5);
        var values = context.GetSma(parameters.GetInt("period", 20));
        if (currentIndex < lookback || values[currentIndex - lookback] == 0) return (0, 0);
        var current = PercentChange(values[currentIndex - lookback], values[currentIndex]);
        var previous = previousIndex < lookback || values[previousIndex - lookback] == 0
            ? 0
            : PercentChange(values[previousIndex - lookback], values[previousIndex]);
        return (current, previous);
    }

    private static (decimal, decimal) CandleBody(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        (BodyRatio(context.Bars[currentIndex]), BodyRatio(context.Bars[previousIndex]));

    private static decimal BodyRatio(OhlcvBar bar)
    {
        var range = bar.High - bar.Low;
        return range == 0 ? 0 : Math.Abs(bar.Close - bar.Open) / range;
    }

    private static (decimal, decimal) AtrPercent(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = context.GetAtr(parameters.GetInt("period", 14));
        return (
            context.Closes[currentIndex] == 0 ? 0 : values[currentIndex] / context.Closes[currentIndex] * 100,
            context.Closes[previousIndex] == 0 ? 0 : values[previousIndex] / context.Closes[previousIndex] * 100);
    }

    private static (decimal, decimal) Volatility(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        if (currentIndex < period) return (0, 0);
        return (
            RuleIndicatorMath.CalculateVolatility(
                context.Closes,
                currentIndex,
                period,
                context.Bars[currentIndex].TimeFrame),
            RuleIndicatorMath.CalculateVolatility(
                context.Closes,
                previousIndex,
                period,
                context.Bars[previousIndex].TimeFrame));
    }
}

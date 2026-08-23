using StockTrader.Engine.MarketData;

namespace StockTrader.Engine.Rules;

internal static partial class StandardRuleIndicatorCalculators
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
}

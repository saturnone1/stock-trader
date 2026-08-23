using StockTrader.Engine.MarketData;

namespace StockTrader.Engine.Rules;

internal static partial class StandardRuleIndicatorCalculators
{
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

    private static decimal AveragePriorVolume(PriceBar[] bars, int index, int period)
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

    private static decimal BodyRatio(PriceBar bar)
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

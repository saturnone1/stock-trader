namespace StockTrader.Services.Patterns;

internal static class MomentumVolumeRuleIndicatorCalculators
{
    internal static IReadOnlyDictionary<string, RuleIndicatorCalculator> All { get; } =
        new Dictionary<string, RuleIndicatorCalculator>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONSECUTIVE_UP"] = ConsecutiveUp,
            ["CONSECUTIVE_DOWN"] = ConsecutiveDown,
            ["ADX"] = Adx,
            ["STOCHASTIC_K"] = StochasticK,
            ["STOCHASTIC_D"] = StochasticD,
            ["OBV"] = Obv,
            ["PRICE_VS_VWAP"] = PriceVsVwap,
            ["OBV_SLOPE"] = ObvSlope,
            ["CCI"] = Cci,
            ["ROC"] = Roc,
            ["WILLIAMS_R"] = WilliamsR,
            ["CMF"] = Cmf
        };

    private static (decimal, decimal) ConsecutiveUp(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        (CountDirection(context.Closes, currentIndex, (current, previous) => current > previous),
         CountDirection(context.Closes, previousIndex, (current, previous) => current > previous));

    private static (decimal, decimal) ConsecutiveDown(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        (CountDirection(context.Closes, currentIndex, (current, previous) => current < previous),
         CountDirection(context.Closes, previousIndex, (current, previous) => current < previous));

    private static int CountDirection(
        decimal[] closes,
        int index,
        Func<decimal, decimal, bool> continues)
    {
        var count = 0;
        for (var i = index; i > 0 && i > index - 30; i--)
        {
            if (!continues(closes[i], closes[i - 1])) break;
            count++;
        }
        return count;
    }

    private static (decimal, decimal) Adx(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = RuleIndicatorMath.ComputeAdx(context.Bars, parameters.GetInt("period", 14));
        return (
            currentIndex < values.Length ? values[currentIndex] : 0,
            previousIndex < values.Length ? values[previousIndex] : 0);
    }

    private static (decimal, decimal) StochasticK(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var (k, _) = RuleIndicatorMath.ComputeStochastic(
            context.Bars,
            parameters.GetInt("period", 14),
            3);
        return (
            currentIndex < k.Length ? k[currentIndex] : 0,
            previousIndex < k.Length ? k[previousIndex] : 0);
    }

    private static (decimal, decimal) StochasticD(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var (_, d) = RuleIndicatorMath.ComputeStochastic(
            context.Bars,
            parameters.GetInt("period", 14),
            parameters.GetInt("smooth", 3));
        return (
            currentIndex < d.Length ? d[currentIndex] : 0,
            previousIndex < d.Length ? d[previousIndex] : 0);
    }

    private static (decimal, decimal) Obv(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var values = context.GetObv();
        return (values[currentIndex], values[previousIndex]);
    }

    private static (decimal, decimal) PriceVsVwap(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        var currentVwap = RuleIndicatorMath.CalculateRollingVwap(context.Bars, currentIndex, period);
        var previousVwap = RuleIndicatorMath.CalculateRollingVwap(context.Bars, previousIndex, period);
        if (currentVwap == 0) return (0, 0);
        return (
            (context.Closes[currentIndex] - currentVwap) / currentVwap * 100,
            previousVwap == 0
                ? 0
                : (context.Closes[previousIndex] - previousVwap) / previousVwap * 100);
    }

    private static (decimal, decimal) ObvSlope(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var lookback = parameters.GetInt("lookback", 5);
        var values = context.GetObv();
        if (currentIndex < lookback || values[currentIndex - lookback] == 0) return (0, 0);
        var current = (values[currentIndex] - values[currentIndex - lookback])
            / Math.Abs(values[currentIndex - lookback]) * 100;
        var previous = previousIndex < lookback || values[previousIndex - lookback] == 0
            ? 0
            : (values[previousIndex] - values[previousIndex - lookback])
                / Math.Abs(values[previousIndex - lookback]) * 100;
        return (current, previous);
    }

    private static (decimal, decimal) Cci(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        return (
            RuleIndicatorMath.CalculateCci(context.Bars, currentIndex, period),
            RuleIndicatorMath.CalculateCci(context.Bars, previousIndex, period));
    }

    private static (decimal, decimal) Roc(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 14);
        return (
            RateOfChange(context.Closes, currentIndex, period),
            RateOfChange(context.Closes, previousIndex, period));
    }

    private static decimal RateOfChange(decimal[] closes, int index, int period) =>
        index >= period && closes[index - period] != 0
            ? (closes[index] - closes[index - period]) / closes[index - period] * 100
            : 0;

    private static (decimal, decimal) WilliamsR(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 14);
        return (
            RuleIndicatorMath.CalculateWilliamsR(context.Bars, currentIndex, period),
            RuleIndicatorMath.CalculateWilliamsR(context.Bars, previousIndex, period));
    }

    private static (decimal, decimal) Cmf(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        return (
            RuleIndicatorMath.CalculateCmf(context.Bars, currentIndex, period),
            RuleIndicatorMath.CalculateCmf(context.Bars, previousIndex, period));
    }
}

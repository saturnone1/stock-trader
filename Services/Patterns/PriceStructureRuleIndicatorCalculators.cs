using StockTrader.Models;

namespace StockTrader.Services.Patterns;

internal static class PriceStructureRuleIndicatorCalculators
{
    internal static IReadOnlyDictionary<string, RuleIndicatorCalculator> All { get; } =
        new Dictionary<string, RuleIndicatorCalculator>(StringComparer.OrdinalIgnoreCase)
        {
            ["DIST_FROM_HIGH"] = DistanceFromHigh,
            ["DIST_FROM_LOW"] = DistanceFromLow,
            ["BREAKOUT_HIGH"] = BreakoutHigh,
            ["BREAKOUT_LOW"] = BreakoutLow,
            ["GAP"] = Gap,
            ["HIGHER_LOW"] = HigherLow,
            ["LOWER_HIGH"] = LowerHigh,
            ["INSIDE_BAR"] = InsideBar,
            ["ENGULFING"] = Engulfing
        };

    private static (decimal, decimal) DistanceFromHigh(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 52);
        var currentHigh = Highest(context.Bars, Math.Max(0, currentIndex - period + 1), currentIndex);
        if (currentHigh == 0) return (0, 0);
        var previousHigh = Highest(context.Bars, Math.Max(0, previousIndex - period + 1), previousIndex);
        return (
            (currentHigh - context.Closes[currentIndex]) / currentHigh * 100,
            previousHigh == 0 ? 0 : (previousHigh - context.Closes[previousIndex]) / previousHigh * 100);
    }

    private static (decimal, decimal) DistanceFromLow(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 52);
        var currentLow = Lowest(context.Bars, Math.Max(0, currentIndex - period + 1), currentIndex);
        if (currentLow == decimal.MaxValue || currentLow == 0) return (0, 0);
        var previousLow = Lowest(context.Bars, Math.Max(0, previousIndex - period + 1), previousIndex);
        return (
            (context.Closes[currentIndex] - currentLow) / currentLow * 100,
            previousLow == decimal.MaxValue || previousLow == 0
                ? 0
                : (context.Closes[previousIndex] - previousLow) / previousLow * 100);
    }

    private static (decimal, decimal) BreakoutHigh(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        var currentHigh = Highest(context.Bars, Math.Max(0, currentIndex - period), currentIndex - 1);
        var previousHigh = Highest(context.Bars, Math.Max(0, previousIndex - period), previousIndex - 1);
        return (
            context.Closes[currentIndex] > currentHigh ? 1m : 0m,
            context.Closes[previousIndex] > previousHigh ? 1m : 0m);
    }

    private static (decimal, decimal) BreakoutLow(
        RuleIndicatorParameters parameters,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        var period = parameters.GetInt("period", 20);
        var currentLow = Lowest(context.Bars, Math.Max(0, currentIndex - period), currentIndex - 1);
        var previousLow = Lowest(context.Bars, Math.Max(0, previousIndex - period), previousIndex - 1);
        return (
            context.Closes[currentIndex] < currentLow ? 1m : 0m,
            context.Closes[previousIndex] < previousLow ? 1m : 0m);
    }

    private static decimal Highest(OhlcvBar[] bars, int start, int end)
    {
        decimal highest = 0;
        for (var i = start; i <= end; i++)
            if (bars[i].High > highest) highest = bars[i].High;
        return highest;
    }

    private static decimal Lowest(OhlcvBar[] bars, int start, int end)
    {
        var lowest = decimal.MaxValue;
        for (var i = start; i <= end; i++)
            if (bars[i].Low < lowest) lowest = bars[i].Low;
        return lowest;
    }

    private static (decimal, decimal) Gap(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        if (currentIndex < 1) return (0, 0);
        return (
            GapPercent(context.Bars, context.Closes, currentIndex),
            previousIndex < 1 ? 0 : GapPercent(context.Bars, context.Closes, previousIndex));
    }

    private static decimal GapPercent(OhlcvBar[] bars, decimal[] closes, int index) =>
        closes[index - 1] == 0
            ? 0
            : (bars[index].Open - closes[index - 1]) / closes[index - 1] * 100;

    private static (decimal, decimal) HigherLow(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        (CountSequence(context.Bars, currentIndex, 20, (current, previous) => current.Low > previous.Low),
         CountSequence(context.Bars, previousIndex, 20, (current, previous) => current.Low > previous.Low));

    private static (decimal, decimal) LowerHigh(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex) =>
        (CountSequence(context.Bars, currentIndex, 20, (current, previous) => current.High < previous.High),
         CountSequence(context.Bars, previousIndex, 20, (current, previous) => current.High < previous.High));

    private static int CountSequence(
        OhlcvBar[] bars,
        int index,
        int maximum,
        Func<OhlcvBar, OhlcvBar, bool> continues)
    {
        var count = 0;
        for (var i = index; i > 0 && i > index - maximum; i--)
        {
            if (!continues(bars[i], bars[i - 1])) break;
            count++;
        }
        return count;
    }

    private static (decimal, decimal) InsideBar(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        if (currentIndex < 1) return (0, 0);
        return (
            IsInside(context.Bars, currentIndex) ? 1m : 0m,
            previousIndex >= 1 && IsInside(context.Bars, previousIndex) ? 1m : 0m);
    }

    private static bool IsInside(OhlcvBar[] bars, int index) =>
        bars[index].High <= bars[index - 1].High && bars[index].Low >= bars[index - 1].Low;

    private static (decimal, decimal) Engulfing(
        RuleIndicatorParameters _,
        RuleIndicatorEvaluationContext context,
        int currentIndex,
        int previousIndex)
    {
        if (currentIndex < 1) return (0, 0);
        return (
            EngulfingValue(context.Bars[currentIndex], context.Bars[currentIndex - 1]),
            previousIndex >= 1
                ? EngulfingValue(context.Bars[previousIndex], context.Bars[previousIndex - 1])
                : 0m);
    }

    private static decimal EngulfingValue(OhlcvBar current, OhlcvBar previous)
    {
        var currentBullish = current.Close > current.Open;
        var previousBullish = previous.Close > previous.Open;
        if (currentBullish && !previousBullish &&
            current.Close > previous.Open && current.Open < previous.Close) return 1m;
        if (!currentBullish && previousBullish &&
            current.Close < previous.Open && current.Open > previous.Close) return -1m;
        return 0m;
    }
}

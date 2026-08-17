using StockTrader.Domain.MarketData;
using StockTrader.Models;
using EvalContext = StockTrader.Services.Patterns.RuleIndicatorEvaluator.EvalContext;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Deterministically resolves the initial protective stop and profit target for a custom strategy.
/// The caller remains responsible for rejecting levels that are invalid for a long position.
/// </summary>
internal static class DynamicExitPricePolicy
{
    public static DynamicExitPriceLevels Resolve(
        DynamicExitConfig? config,
        decimal defaultStopAtrMultiplier,
        decimal defaultTargetAtrMultiplier,
        OhlcvBar[] bars,
        EvalContext context,
        decimal currentAtr)
    {
        var current = bars[^1];
        if (config == null)
        {
            return new DynamicExitPriceLevels(
                current.Close - currentAtr * defaultStopAtrMultiplier,
                current.Close + currentAtr * defaultTargetAtrMultiplier);
        }

        var stop = ResolveStop(
            config,
            defaultStopAtrMultiplier,
            current,
            bars,
            context,
            currentAtr);
        var target = ResolveTarget(
            config,
            defaultTargetAtrMultiplier,
            current,
            bars,
            context,
            currentAtr,
            current.Close - stop);
        return new DynamicExitPriceLevels(stop, target);
    }

    private static decimal ResolveStop(
        DynamicExitConfig config,
        decimal defaultAtrMultiplier,
        OhlcvBar current,
        OhlcvBar[] bars,
        EvalContext context,
        decimal currentAtr)
    {
        decimal Parameter(string key, decimal defaultValue) =>
            config.StopParams.TryGetValue(key, out var value) ? value : defaultValue;

        return config.StopType.ToUpperInvariant() switch
        {
            "BOLLINGER_LOWER" => context
                .GetBollinger((int)Parameter("period", 20), Parameter("stddev", 2m)).Lower[^1],
            "SMA" => context.GetSma((int)Parameter("period", 20))[^1],
            "EMA" => context.GetEma((int)Parameter("period", 20))[^1],
            "PREV_LOW" => PreviousLow(bars, (int)Parameter("period", 5)),
            "PERCENT" => current.Close * (1 - Parameter("percent", 2m) / 100m),
            _ => current.Close - currentAtr * Parameter("multiplier", defaultAtrMultiplier)
        };
    }

    private static decimal ResolveTarget(
        DynamicExitConfig config,
        decimal defaultAtrMultiplier,
        OhlcvBar current,
        OhlcvBar[] bars,
        EvalContext context,
        decimal currentAtr,
        decimal riskDistance)
    {
        decimal Parameter(string key, decimal defaultValue) =>
            config.TargetParams.TryGetValue(key, out var value) ? value : defaultValue;

        return config.TargetType.ToUpperInvariant() switch
        {
            "BOLLINGER_UPPER" => context
                .GetBollinger((int)Parameter("period", 20), Parameter("stddev", 2m)).Upper[^1],
            "SMA" => context.GetSma((int)Parameter("period", 20))[^1],
            "EMA" => context.GetEma((int)Parameter("period", 20))[^1],
            "PREV_HIGH" => PreviousHigh(bars, (int)Parameter("period", 5)),
            "R_MULTIPLE" => current.Close + riskDistance * Parameter("multiple", 3m),
            "PERCENT" => current.Close * (1 + Parameter("percent", 5m) / 100m),
            _ => current.Close + currentAtr * Parameter("multiplier", defaultAtrMultiplier)
        };
    }

    private static decimal PreviousLow(OhlcvBar[] bars, int lookback)
    {
        var low = decimal.MaxValue;
        for (var index = Math.Max(0, bars.Length - 1 - lookback); index < bars.Length - 1; index++)
        {
            if (bars[index].Low < low)
                low = bars[index].Low;
        }

        return low == decimal.MaxValue ? bars[^1].Close * 0.98m : low;
    }

    private static decimal PreviousHigh(OhlcvBar[] bars, int lookback)
    {
        decimal high = 0;
        for (var index = Math.Max(0, bars.Length - 1 - lookback); index < bars.Length - 1; index++)
        {
            if (bars[index].High > high)
                high = bars[index].High;
        }

        return high == 0 ? bars[^1].Close * 1.05m : high;
    }
}

internal readonly record struct DynamicExitPriceLevels(decimal Stop, decimal Target);

using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class DynamicExitPricePolicyTests
{
    [Fact]
    public void Resolve_UsesStrategyAtrDefaultsWhenNoDynamicConfigurationExists()
    {
        var (bars, context) = Context();

        var result = DynamicExitPricePolicy.Resolve(null, 2m, 3m, bars, context, 4m);

        result.Should().Be(new DynamicExitPriceLevels(92m, 112m));
    }

    [Fact]
    public void Resolve_PercentStopAndRMultipleTargetShareTheResolvedRiskDistance()
    {
        var (bars, context) = Context();
        var config = new DynamicExitConfig
        {
            StopType = "PERCENT",
            StopParams = new Dictionary<string, decimal> { ["percent"] = 5m },
            TargetType = "R_MULTIPLE",
            TargetParams = new Dictionary<string, decimal> { ["multiple"] = 2m }
        };

        var result = DynamicExitPricePolicy.Resolve(config, 2m, 3m, bars, context, 4m);

        result.Should().Be(new DynamicExitPriceLevels(95m, 110m));
    }

    [Fact]
    public void Resolve_PreviousRangeExcludesTheCurrentBar()
    {
        var (bars, context) = Context();
        bars[^3].Low = 80m;
        bars[^3].High = 120m;
        bars[^1].Low = 70m;
        bars[^1].High = 130m;
        var config = new DynamicExitConfig
        {
            StopType = "PREV_LOW",
            StopParams = new Dictionary<string, decimal> { ["period"] = 2m },
            TargetType = "PREV_HIGH",
            TargetParams = new Dictionary<string, decimal> { ["period"] = 2m }
        };

        var result = DynamicExitPricePolicy.Resolve(config, 2m, 3m, bars, context, 4m);

        result.Should().Be(new DynamicExitPriceLevels(80m, 120m));
    }

    [Fact]
    public void Resolve_IndicatorBasedLevelsUseTheSharedEvaluationContext()
    {
        var closes = Enumerable.Range(0, 60).Select(index => 80m + index / 3m).ToArray();
        var (bars, context) = Context(closes);
        var bollinger = context.GetBollinger(20, 2m);

        DynamicExitPricePolicy.Resolve(
                Config("BOLLINGER_LOWER", "BOLLINGER_UPPER"), 2m, 3m, bars, context, 4m)
            .Should().Be(new DynamicExitPriceLevels(bollinger.Lower[^1], bollinger.Upper[^1]));
        DynamicExitPricePolicy.Resolve(
                Config("SMA", "SMA"), 2m, 3m, bars, context, 4m)
            .Should().Be(new DynamicExitPriceLevels(context.GetSma(20)[^1], context.GetSma(20)[^1]));
        DynamicExitPricePolicy.Resolve(
                Config("EMA", "EMA"), 2m, 3m, bars, context, 4m)
            .Should().Be(new DynamicExitPriceLevels(context.GetEma(20)[^1], context.GetEma(20)[^1]));
    }

    private static DynamicExitConfig Config(string stopType, string targetType) => new()
    {
        StopType = stopType,
        StopParams = new Dictionary<string, decimal> { ["period"] = 20m, ["stddev"] = 2m },
        TargetType = targetType,
        TargetParams = new Dictionary<string, decimal> { ["period"] = 20m, ["stddev"] = 2m }
    };

    private static (OhlcvBar[] Bars, RuleIndicatorEvaluationContext Context) Context(
        decimal[]? closes = null)
    {
        closes ??= Enumerable.Repeat(100m, 60).ToArray();
        var start = new DateTime(2024, 1, 1);
        var bars = closes.Select((close, index) => new OhlcvBar
        {
            Timestamp = start.AddDays(index),
            Open = close,
            High = close + 1m,
            Low = close - 1m,
            Close = close,
            Volume = 100_000
        }).ToArray();
        var context = new RuleIndicatorEvaluator(new IndicatorService()).CreateContext(bars);
        return (bars, context);
    }
}

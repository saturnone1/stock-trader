using FluentAssertions;
using StockTrader.Domain.Strategies;
using StockTrader.Engine.MarketData;

namespace StockTrader.Tests;

public class RuleIndicatorEvaluatorTests
{
    [Fact]
    public void CalculatorRegistryCoversEveryCentralCatalogIndicatorExactlyOnce()
    {
        RuleIndicatorCalculatorRegistry.Codes.Should().BeEquivalentTo(
            IndicatorCatalog.All.Select(descriptor => descriptor.Code));
        RuleIndicatorCalculatorRegistry.Codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Compute_UsesCatalogDefaultsAndCachesIndicatorWithinEvaluationContext()
    {
        var evaluator = new RuleIndicatorEvaluator();
        var context = evaluator.CreateContext(CreateBars(
            Enumerable.Range(0, 60).Select(value => 100m + value).ToArray()));

        var first = evaluator.Compute("rsi", [], context, 0);
        var second = evaluator.Compute("RSI", [], context, 1);
        var cached = context.GetRsi(14);

        first.Should().Be((cached[^1], cached[^2]));
        second.Should().Be((cached[^2], cached[^3]));
        context.GetRsi(14).Should().BeSameAs(cached);
    }

    [Fact]
    public void CreateContext_DoesNotLeakCachedValuesAcrossSymbols()
    {
        var evaluator = new RuleIndicatorEvaluator();
        var first = evaluator.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray()));
        var second = evaluator.CreateContext(CreateBars(Enumerable.Repeat(200m, 60).ToArray()));

        first.GetSma(20).Should().NotBeSameAs(second.GetSma(20));
        first.GetSma(20)[^1].Should().Be(100m);
        second.GetSma(20)[^1].Should().Be(200m);
    }

    [Fact]
    public void Compute_PreservesCurrentAndPreviousBarOffsetContract()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[57] = 100m;
        closes[58] = 110m;
        closes[59] = 121m;
        var evaluator = new RuleIndicatorEvaluator();
        var context = evaluator.CreateContext(CreateBars(closes));

        evaluator.Compute("PRICE_CHANGE", new() { ["bars"] = 1 }, context, 0).Should().Be((10m, 10m));
        evaluator.Compute("PRICE_CHANGE", new() { ["bars"] = 1 }, context, 1).Should().Be((10m, 0m));
    }

    [Fact]
    public void Compute_UnknownIndicatorReturnsNeutralValues()
    {
        var evaluator = new RuleIndicatorEvaluator();
        var context = evaluator.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray()));

        evaluator.Compute("NOT_REGISTERED", [], context, 0).Should().Be((0m, 0m));
    }

    private static PriceBar[] CreateBars(IReadOnlyList<decimal> closes)
    {
        var start = new DateTime(2024, 1, 1);
        return closes.Select((close, index) => new PriceBar(
            start.AddDays(index), TimeFrame.Daily, close, close + 1m,
            close - 1m, close, 100_000)).ToArray();
    }
}

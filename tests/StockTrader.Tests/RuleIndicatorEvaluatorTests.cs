using FluentAssertions;
using Moq;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

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
        var values = Enumerable.Range(0, 60).Select(value => (decimal)value).ToArray();
        var indicators = new Mock<IIndicatorService>();
        indicators.Setup(service => service.RSI(It.IsAny<decimal[]>(), 14)).Returns(values);
        var evaluator = new RuleIndicatorEvaluator(indicators.Object);
        var context = evaluator.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray()));

        var first = evaluator.Compute("rsi", [], context, 0);
        var second = evaluator.Compute("RSI", [], context, 1);

        first.Should().Be((59m, 58m));
        second.Should().Be((58m, 57m));
        indicators.Verify(service => service.RSI(It.IsAny<decimal[]>(), 14), Times.Once);
    }

    [Fact]
    public void CreateContext_DoesNotLeakCachedValuesAcrossSymbols()
    {
        var indicators = new Mock<IIndicatorService>();
        indicators.Setup(service => service.SMA(It.IsAny<decimal[]>(), 20))
            .Returns((decimal[] closes, int _) => Enumerable.Repeat(closes[^1], closes.Length).ToArray());
        var evaluator = new RuleIndicatorEvaluator(indicators.Object);

        evaluator.Compute("PRICE_VS_SMA", [], evaluator.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray())), 0);
        evaluator.Compute("PRICE_VS_SMA", [], evaluator.CreateContext(CreateBars(Enumerable.Repeat(200m, 60).ToArray())), 0);

        indicators.Verify(service => service.SMA(It.IsAny<decimal[]>(), 20), Times.Exactly(2));
    }

    [Fact]
    public void Compute_PreservesCurrentAndPreviousBarOffsetContract()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[57] = 100m;
        closes[58] = 110m;
        closes[59] = 121m;
        var evaluator = new RuleIndicatorEvaluator(new IndicatorService());
        var context = evaluator.CreateContext(CreateBars(closes));

        evaluator.Compute("PRICE_CHANGE", new() { ["bars"] = 1 }, context, 0).Should().Be((10m, 10m));
        evaluator.Compute("PRICE_CHANGE", new() { ["bars"] = 1 }, context, 1).Should().Be((10m, 0m));
    }

    [Fact]
    public void Compute_UnknownIndicatorReturnsNeutralValues()
    {
        var evaluator = new RuleIndicatorEvaluator(new IndicatorService());
        var context = evaluator.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray()));

        evaluator.Compute("NOT_REGISTERED", [], context, 0).Should().Be((0m, 0m));
    }

    private static OhlcvBar[] CreateBars(IReadOnlyList<decimal> closes)
    {
        var start = new DateTime(2024, 1, 1);
        return closes.Select((close, index) => new OhlcvBar
        {
            Timestamp = start.AddDays(index),
            Open = close,
            High = close + 1m,
            Low = close - 1m,
            Close = close,
            Volume = 100_000
        }).ToArray();
    }
}

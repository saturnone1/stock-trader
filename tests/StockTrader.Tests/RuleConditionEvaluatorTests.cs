using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class RuleConditionEvaluatorTests
{
    [Theory]
    [InlineData(11, 9, ">", 10, 10, true)]
    [InlineData(9, 11, "<", 10, 10, true)]
    [InlineData(10, 9, ">=", 10, 10, true)]
    [InlineData(10, 11, "<=", 10, 10, true)]
    [InlineData(11, 10, "crosses_above", 10, 10, true)]
    [InlineData(10, 9, "crosses_above", 10, 10, false)]
    [InlineData(9, 10, "crosses_below", 10, 10, true)]
    [InlineData(10, 11, "crosses_below", 10, 10, false)]
    [InlineData(11, 9, "unsupported", 10, 10, false)]
    public void Compare_PreservesOperatorBoundarySemantics(
        int current,
        int previous,
        string comparisonOperator,
        int threshold,
        int previousThreshold,
        bool expected)
    {
        RuleConditionEvaluator.Compare(
                current,
                previous,
                comparisonOperator,
                threshold,
                previousThreshold)
            .Should().Be(expected);
    }

    [Fact]
    public void Evaluate_ReferenceSymbolCannotReadPastTheExplicitAsOfBoundary()
    {
        var indicators = new RuleIndicatorEvaluator(new IndicatorService());
        var sut = new RuleConditionEvaluator(indicators);
        var mainBars = CreateBars(Enumerable.Repeat(100m, 60).ToArray());
        var referenceBars = CreateBars(Enumerable.Repeat(100m, 61).ToArray());
        referenceBars[^1].Close = 120m;
        referenceBars[^1].High = 121m;
        var rule = PriceChangeRule(">", 5m, 1m);
        rule.RefSymbol = "spy";

        var result = sut.Evaluate(
            rule,
            indicators.CreateContext(mainBars),
            new Dictionary<string, OhlcvBar[]> { ["SPY"] = referenceBars },
            mainBars[^1].Timestamp);

        result.IsMatch.Should().BeFalse();
        result.Details.Should().StartWith("spy:PRICE_CHANGE=");
    }

    [Fact]
    public void Evaluate_GroupsOwnsNestedLogicWeightAndExplanationAggregation()
    {
        var indicators = new RuleIndicatorEvaluator(new IndicatorService());
        var conditions = new RuleConditionEvaluator(indicators);
        var sut = new RuleGroupEvaluator(conditions);
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[^1] = 110m;
        var context = indicators.CreateContext(CreateBars(closes));
        var groups = new[]
        {
            new ConditionGroup
            {
                Label = "약세",
                Logic = "AND",
                Rules = [PriceChangeRule("<", -5m, 3m)]
            },
            new ConditionGroup
            {
                Label = "강세",
                Logic = "AND",
                Rules = [PriceChangeRule(">", 5m, 1m)]
            }
        };

        var result = sut.Evaluate(groups, "OR", context);

        result.IsMatch.Should().BeTrue();
        result.MatchedWeight.Should().Be(1m);
        result.TotalWeight.Should().Be(4m);
        result.Details.Should().StartWith("[강세] PRICE_CHANGE=");
    }

    [Fact]
    public void Evaluate_EmptyAndGroupSetDoesNotPassVacuously()
    {
        var indicators = new RuleIndicatorEvaluator(new IndicatorService());
        var sut = new RuleGroupEvaluator(new RuleConditionEvaluator(indicators));

        var result = sut.Evaluate(
            Array.Empty<ConditionGroup>(),
            "AND",
            indicators.CreateContext(CreateBars(Enumerable.Repeat(100m, 60).ToArray())));

        result.IsMatch.Should().BeFalse();
        result.MatchedWeight.Should().Be(0m);
        result.TotalWeight.Should().Be(0m);
    }

    private static EntryRule PriceChangeRule(string comparisonOperator, decimal value, decimal weight) => new()
    {
        Indicator = "PRICE_CHANGE",
        Operator = comparisonOperator,
        Value = value,
        Weight = weight,
        Params = new Dictionary<string, decimal> { ["bars"] = 1 }
    };

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

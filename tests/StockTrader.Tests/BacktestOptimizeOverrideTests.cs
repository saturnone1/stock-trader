using System.Text.Json;
using FluentAssertions;
using StockTrader.Api;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestOptimizeOverrideTests
{
    [Fact]
    public void ApplyOptimizeOverrides_UpdatesCompareIndicatorAndCompareParams()
    {
        var pattern = new CustomPatternDefinition
        {
            Name = "compare-sync",
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Params = new Dictionary<string, decimal> { ["bars"] = 1 },
                    Operator = ">",
                    Value = 3,
                    CompareIndicator = "RSI",
                    CompareParams = new Dictionary<string, decimal> { ["period"] = 14 },
                    Weight = 1,
                    WithinBars = 0,
                    ConsecutiveBars = 0,
                }
            })
        };

        var snapshot = new OptimizeParamSnapshot
        {
            RuleOverrides = new List<RuleOverrideEntry>
            {
                new() { RuleIndex = 0, ParamKey = "compare.period", Value = 21 }
            },
            RuleFieldOverrides = new List<RuleFieldOverrideEntry>
            {
                new() { RuleIndex = 0, FieldName = "compareIndicator", StringValue = "ROC" },
                new() { RuleIndex = 0, FieldName = "weight", NumericValue = 1.5m },
                new() { RuleIndex = 0, FieldName = "withinBars", NumericValue = 3 },
                new() { RuleIndex = 0, FieldName = "consecutiveBars", NumericValue = 2 },
            }
        };

        BacktestService.ApplyOptimizeOverrides(pattern, snapshot);

        var rules = JsonSerializer.Deserialize<List<EntryRule>>(pattern.EntryRulesJson);
        rules.Should().NotBeNull();
        rules!.Should().ContainSingle();
        rules[0].CompareIndicator.Should().Be("ROC");
        rules[0].CompareParams["period"].Should().Be(21);
        rules[0].Weight.Should().Be(1.5m);
        rules[0].WithinBars.Should().Be(3);
        rules[0].ConsecutiveBars.Should().Be(2);
    }
}

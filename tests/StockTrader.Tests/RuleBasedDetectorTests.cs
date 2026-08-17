using System.Text.Json;
using FluentAssertions;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class RuleBasedDetectorTests
{
    private static readonly MarketRegime BullRegime = new()
    {
        SpyAbove200Ma = true,
        SpyPrice = 500m,
        Spy200Ma = 450m,
        VixLevel = 15m
    };

    [Fact]
    public async Task DetectAsync_WithinBarsChecksFullRequestedWindow()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[44] = 110m;
        closes[45] = 100m;

        var sut = CreateSut(new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">",
            Value = 5m,
            WithinBars = 20,
            Params = new Dictionary<string, decimal> { ["bars"] = 1 }
        });

        var result = await sut.DetectAsync("AAPL", CreateBars(closes), BullRegime);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_ConsecutiveBarsFailsWhenOlderRequiredBarsDoNotMatch()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        for (int i = 50; i < 60; i++)
            closes[i] = closes[i - 1] * 1.06m;

        var sut = CreateSut(new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">",
            Value = 5m,
            ConsecutiveBars = 12,
            Params = new Dictionary<string, decimal> { ["bars"] = 1 }
        });

        var result = await sut.DetectAsync("AAPL", CreateBars(closes), BullRegime);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_supports_cumulative_rsi_rules_in_pattern_builder()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[55] = 96m;
        closes[56] = 93m;
        closes[57] = 90m;
        closes[58] = 88m;
        closes[59] = 87m;

        var sut = CreateSut(new EntryRule
        {
            Indicator = "CUMULATIVE_RSI",
            Operator = "<=",
            Value = 10m,
            Params = new Dictionary<string, decimal>
            {
                ["period"] = 2,
                ["cumulativePeriod"] = 2
            }
        });

        var result = await sut.DetectAsync("AAPL", CreateBars(closes), BullRegime);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ShouldExit_UsesGroupedExitLogic()
    {
        var bars = CreateBars(Enumerable.Range(0, 60).Select(index => index == 59 ? 110m : 100m).ToArray());
        var falseGroup = new ConditionGroup
        {
            Label = "false",
            Logic = "AND",
            Rules = [new EntryRule { Indicator = "PRICE_CHANGE", Operator = "<", Value = -5m, Params = new() { ["bars"] = 1 } }]
        };
        var trueGroup = new ConditionGroup
        {
            Label = "true",
            Logic = "AND",
            Rules = [new EntryRule { Indicator = "PRICE_CHANGE", Operator = ">", Value = 5m, Params = new() { ["bars"] = 1 } }]
        };
        var definition = new CustomPatternDefinition
        {
            Name = "grouped-exit",
            EntryRulesJson = JsonSerializer.Serialize(new[] { new EntryRule { Indicator = "PRICE_CHANGE", Operator = ">", Value = -100m } }),
            ExitGroupsJson = JsonSerializer.Serialize(new[] { falseGroup, trueGroup }),
            ExitGroupsLogic = "OR"
        };

        new RuleBasedDetector(new IndicatorService(), definition).ShouldExit(bars).Should().BeTrue();

        definition.ExitGroupsLogic = "AND";
        new RuleBasedDetector(new IndicatorService(), definition).ShouldExit(bars).Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_ReferenceSymbolIgnoresBarsAfterEvaluationDate()
    {
        var mainBars = CreateBars(Enumerable.Repeat(100m, 60).ToArray());
        var referenceBars = CreateBars(Enumerable.Repeat(100m, 61).ToArray());
        referenceBars[^1].Close = 120m;
        referenceBars[^1].High = 121m;
        var sut = CreateSut(new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">",
            Value = 5m,
            RefSymbol = "SPY",
            Params = new() { ["bars"] = 1 }
        });
        sut.SetReferenceData(new Dictionary<string, OhlcvBar[]> { ["SPY"] = referenceBars }, mainBars[^1].Timestamp);

        var result = await sut.DetectAsync("AAPL", mainBars, BullRegime);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_HistoricalVolatilityUsesPercentUnits()
    {
        var closes = Enumerable.Range(0, 60).Select(index => index % 2 == 0 ? 90m : 110m).ToArray();
        var sut = CreateSut(new EntryRule
        {
            Indicator = "VOLATILITY_20D",
            Operator = ">=",
            Value = 30m,
            Params = new() { ["period"] = 20 }
        });

        var result = await sut.DetectAsync("AAPL", CreateBars(closes), BullRegime);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_DistanceFromHighIsAPositiveDrawdownPercent()
    {
        var closes = Enumerable.Repeat(100m, 60).ToArray();
        closes[^1] = 95m;
        var sut = CreateSut(new EntryRule
        {
            Indicator = "DIST_FROM_HIGH",
            Operator = ">=",
            Value = 4m,
            Params = new() { ["period"] = 20 }
        });

        var result = await sut.DetectAsync("AAPL", CreateBars(closes), BullRegime);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_OrConfidenceDoesNotDependOnRuleOrder()
    {
        var passing = new EntryRule { Indicator = "PRICE_CHANGE", Operator = ">", Value = -1m, Weight = 1m, Params = new() { ["bars"] = 1 } };
        var failing = new EntryRule { Indicator = "PRICE_CHANGE", Operator = ">", Value = 50m, Weight = 3m, Params = new() { ["bars"] = 1 } };
        var bars = CreateBars(Enumerable.Repeat(100m, 60).ToArray());

        async Task<PatternSignal?> Detect(params EntryRule[] rules)
        {
            var definition = new CustomPatternDefinition
            {
                Name = "or-order",
                EntryRulesJson = JsonSerializer.Serialize(rules),
                EntryLogic = "OR"
            };
            return await new RuleBasedDetector(new IndicatorService(), definition)
                .DetectAsync("AAPL", bars, BullRegime);
        }

        var first = await Detect(passing, failing);
        var second = await Detect(failing, passing);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Confidence.Should().Be(0.25m);
        second!.Confidence.Should().Be(first.Confidence);
    }

    private static RuleBasedDetector CreateSut(EntryRule rule)
    {
        var definition = new CustomPatternDefinition
        {
            Name = "history-window-test",
            EntryRulesJson = JsonSerializer.Serialize(new[] { rule }),
            EntryLogic = "AND",
            AtrStopMultiplier = 2m,
            AtrTargetMultiplier = 3m,
            DefaultAllocationPercent = 100m
        };

        return new RuleBasedDetector(new IndicatorService(), definition);
    }

    private static OhlcvBar[] CreateBars(IReadOnlyList<decimal> closes)
    {
        var start = new DateTime(2024, 1, 1);
        return closes.Select((close, index) => new OhlcvBar
        {
            Timestamp = start.AddDays(index),
            Open = close,
            High = close + 1m,
            Low = Math.Max(1m, close - 1m),
            Close = close,
            Volume = 100_000
        }).ToArray();
    }
}

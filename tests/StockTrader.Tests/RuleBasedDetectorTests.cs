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

using FluentAssertions;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class StrategyOptimizationModuleTests
{
    [Fact]
    public void OptimizeRequestCodecPromotesLegacyEntityIdAndWritesOnlyDocumentReference()
    {
        const string legacy = """
            {
              "BasePattern": { "Id": 42, "Name": "기존 작업", "CreatedAt": "2025-01-01T00:00:00Z" },
              "Symbols": ["TQQQ"],
              "From": "2024-01-01T00:00:00Z",
              "To": "2025-01-01T00:00:00Z",
              "OptimizeParams": {}
            }
            """;

        var request = OptimizeRequestJsonCodec.Deserialize(legacy);

        request.Should().NotBeNull();
        request!.BasePattern.StoredStrategyId.Should().Be(42);
        var current = OptimizeRequestJsonCodec.Serialize(request);
        current.Should().Contain("\"storedStrategyId\":42");
        current.Should().NotContain("\"createdAt\"");
        current.Should().NotContain("\"id\":42");
        OptimizeRequestJsonCodec.Deserialize("not-json").Should().BeNull();
    }

    [Fact]
    public void CloneStrategyDocument_PreservesEveryStrategySetting()
    {
        var source = new StrategyDocument
        {
            StoredStrategyId = 42,
            Name = "clone-golden",
            Description = "all fields",
            EntryRulesJson = "[{\"indicator\":\"RSI\"}]",
            EntryLogic = "OR",
            RequireBullRegime = true,
            AtrStopMultiplier = 1.7m,
            AtrTargetMultiplier = 4.2m,
            MaxHoldingBars = 17,
            TrailingAtr = 1.1m,
            PartialProfitR = 2.3m,
            UseWeightTiers = true,
            WeightTiersJson = "[{\"label\":\"A\"}]",
            DefaultAllocationPercent = 65m,
            ExitRulesJson = "[{\"indicator\":\"ATR\"}]",
            ExitRulesLogic = "AND",
            ExitGroupsJson = "[{\"label\":\"exit\"}]",
            ExitGroupsLogic = "AND",
            ScalingRulesJson = "[{\"direction\":\"SCALE_OUT\"}]",
            TimeFilterJson = "{\"blockedMonths\":[8]}",
            CircuitBreakerJson = "{\"cooldownBars\":7}",
            ReentryJson = "{\"cooldownBarsAfterLoss\":3}",
            PortfolioRulesJson = "{\"maxTotalPositions\":4}",
            EntryGroupsJson = "[{\"label\":\"entry\"}]",
            EntryGroupsLogic = "OR",
            DynamicExitJson = "{\"stopType\":\"SMA\"}",
            EntryMode = "NextOpen",
            TimeFrame = TimeFrame.Weekly,
            SizingMode = "HalfKelly",
            IsActive = false,
            EnableLiveTrading = true,
        };

        var clone = StrategyVariantFactory.CloneStrategyDocument(source);

        clone.Should().BeEquivalentTo(source);
        clone.Should().NotBeSameAs(source);
    }

    [Fact]
    public void GenerateOptimizeCombinations_PreservesTimeFrameAcrossLaterRuleAxes()
    {
        var parameters = new OptimizeParams
        {
            TimeFrameOptions = [(int)TimeFrame.Daily, (int)TimeFrame.Weekly],
            RuleParamOverrides =
            [
                new RuleParamRange
                {
                    RuleIndex = 0,
                    ParamKey = "period",
                    Values = [10m, 20m],
                }
            ],
            RuleFieldOverrides =
            [
                new RuleFieldRange
                {
                    RuleIndex = 0,
                    FieldName = "value",
                    NumericValues = [30m, 40m],
                }
            ],
        };

        var combinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(parameters);

        combinations.Should().HaveCount(8);
        combinations.Select(x => x.TimeFrame).Should().BeEquivalentTo(
            [
                (int)TimeFrame.Daily, (int)TimeFrame.Daily, (int)TimeFrame.Daily, (int)TimeFrame.Daily,
                (int)TimeFrame.Weekly, (int)TimeFrame.Weekly, (int)TimeFrame.Weekly, (int)TimeFrame.Weekly,
            ]);
        combinations.Should().OnlyContain(x => x.RuleOverrides.Count == 1);
        combinations.Should().OnlyContain(x => x.RuleFieldOverrides!.Count == 1);

        combinations[0].RuleOverrides[0].Value = 999m;
        combinations[1].RuleOverrides[0].Value.Should().NotBe(999m,
            "각 후보 스냅샷은 다른 후보와 변경 가능한 객체를 공유하면 안 됩니다");
    }

    [Fact]
    public void GenerateOptimizeCombinations_LargeSearchSpaceIsReproducible()
    {
        var values = Enumerable.Range(1, 10).Select(x => (decimal)x).ToList();
        var parameters = new OptimizeParams
        {
            AtrStopMultiplier = new ParamRange { Values = values },
            AtrTargetMultiplier = new ParamRange { Values = values },
            MaxHoldingBars = new ParamRange { Values = values },
            TrailingAtr = new ParamRange { Values = values },
            PartialProfitR = new ParamRange { Values = values },
        };

        var first = StrategyOptimizationSpace.GenerateOptimizeCombinations(parameters);
        var second = StrategyOptimizationSpace.GenerateOptimizeCombinations(parameters);

        first.Should().HaveCount(50_000);
        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyOptimizeOverrides_UpdatesStrategyAndDetectorTimeFrameTogether()
    {
        var pattern = new StrategyDocument { TimeFrame = TimeFrame.Daily };

        StrategyVariantFactory.ApplyOptimizeOverrides(pattern, new OptimizeParamSnapshot
        {
            TimeFrame = (int)TimeFrame.Weekly,
        });

        pattern.TimeFrame.Should().Be(TimeFrame.Weekly);
    }
}

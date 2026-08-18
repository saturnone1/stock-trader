using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class LongPositionScalingPolicyTests
{
    [Theory]
    [InlineData(10, 15, 1)]
    [InlineData(10, 25, 2)]
    [InlineData(100, 1.5, 1)]
    [InlineData(1, 0.1, 1)]
    public void RequestedQuantity_FloorsFractionalSharesFromInitialQuantity(
        int initialQuantity,
        double percent,
        int expected)
    {
        LongPositionScalingPolicy.RequestedQuantity(initialQuantity, (decimal)percent)
            .Should().Be(expected);
    }

    [Fact]
    public void Apply_ScaleInCapsQuantityAndRecalculatesWeightedEntry()
    {
        var decision = LongPositionScalingPolicy.Apply(
            new LongPositionScalingState(10, 10, 100m, 1_000m),
            StrategyCatalog.ScalingInDirection,
            50m,
            110m,
            maxScaleInQuantity: 2);

        decision.Should().NotBeNull();
        decision!.Action.Should().Be(LongPositionScalingAction.ScaleIn);
        decision.ExecutedQuantity.Should().Be(2);
        decision.State.CurrentQuantity.Should().Be(12);
        decision.State.TotalCost.Should().Be(1_220m);
        decision.State.EntryPrice.Should().Be(1_220m / 12m);
    }

    [Fact]
    public void Apply_ScaleOutUsesOriginalQuantityAndKeepsOneShare()
    {
        var decision = LongPositionScalingPolicy.Apply(
            new LongPositionScalingState(10, 5, 100m, 500m),
            StrategyCatalog.ScalingOutDirection,
            50m,
            110m);

        decision.Should().NotBeNull();
        decision!.Action.Should().Be(LongPositionScalingAction.ScaleOut);
        decision.ExecutedQuantity.Should().Be(4);
        decision.State.CurrentQuantity.Should().Be(1);
        decision.State.EntryPrice.Should().Be(100m);
        decision.State.TotalCost.Should().Be(100m);
    }

    [Fact]
    public void Apply_DoesNotInventExecutionWhenCapitalOrDirectionIsInvalid()
    {
        var state = new LongPositionScalingState(10, 10, 100m, 1_000m);

        LongPositionScalingPolicy.Apply(
                state, StrategyCatalog.ScalingInDirection, 50m, 110m, maxScaleInQuantity: 0)
            .Should().BeNull();
        LongPositionScalingPolicy.Apply(state, "UNKNOWN", 50m, 110m)
            .Should().BeNull();
        LongPositionScalingPolicy.Apply(
                state, StrategyCatalog.ScalingOutDirection, 101m, 110m)
            .Should().BeNull();
    }

    [Fact]
    public void RegisterExecution_IncrementsOnlyAfterAdapterConfirmsAFill()
    {
        var counts = new Dictionary<int, int> { [2] = 1 };

        LongPositionScalingPolicy.RegisterExecution(counts, 2);

        counts.Should().ContainKey(2).WhoseValue.Should().Be(2);
        counts.Should().NotContainKey(0);
    }

    [Fact]
    public void EvaluateScaling_DoesNotConsumeCountWhenCapitalPreventsFill()
    {
        var passingRule = new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">=",
            Value = 0m,
            Params = new Dictionary<string, decimal> { ["bars"] = 1m }
        };
        var strategy = new StrategyDocument
        {
            Name = "no-fill-no-count",
            EntryRulesJson = JsonSerializer.Serialize(new[] { passingRule }),
            ScalingRulesJson = JsonSerializer.Serialize(new[]
            {
                new ScalingRule
                {
                    Direction = StrategyCatalog.ScalingInDirection,
                    Percent = 50m,
                    MaxCount = 1,
                    Conditions = [passingRule]
                }
            })
        };
        var bars = Enumerable.Range(0, 3).Select(index => new OhlcvBar
        {
            Timestamp = new DateTime(2025, 1, 1).AddDays(index),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = 1_000
        }).ToArray();
        var counts = new Dictionary<int, int>();
        ICompiledStrategyRuntime runtime = new RuleBasedDetector(
            new IndicatorService(), strategy);

        var match = runtime.EvaluateScaling(bars, 0m, counts);
        var fill = LongPositionScalingPolicy.Apply(
            new LongPositionScalingState(10, 10, 100m, 1_000m),
            match!.Rule.Direction,
            match.Rule.Percent,
            100m,
            maxScaleInQuantity: 0);

        fill.Should().BeNull();
        counts.Should().BeEmpty();
    }
}

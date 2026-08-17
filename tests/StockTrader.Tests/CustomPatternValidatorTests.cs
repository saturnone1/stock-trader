using System.Text.Json;
using FluentAssertions;
using StockTrader.Models;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class CustomPatternValidatorTests
{
    [Fact]
    public void Validate_RejectsInvalidRiskAndEmptyScalingConditions()
    {
        var pattern = ValidPattern();
        pattern.DefaultAllocationPercent = 120m;
        pattern.ScalingRulesJson = JsonSerializer.Serialize(new[]
        {
            new ScalingRule { Percent = 0m, MaxCount = 0, Conditions = [] }
        });

        var errors = CustomPatternValidator.Validate(pattern);

        errors.Should().Contain(error => error.Contains("0~100%"));
        errors.Should().Contain(error => error.Contains("실행 조건이 비어"));
    }

    [Fact]
    public void Validate_AcceptsGroupedEntryAndExitRules()
    {
        var pattern = ValidPattern();
        pattern.ExitGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup { Label = "청산", Logic = "OR", Rules = [Rule()] }
        });

        CustomPatternValidator.Validate(pattern).Should().BeEmpty();
    }

    [Fact]
    public void Validate_RejectsUnknownIndicatorsAndZeroPeriods()
    {
        var pattern = ValidPattern();
        pattern.EntryGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup
            {
                Label = "진입",
                Logic = "AND",
                Rules =
                [
                    new EntryRule { Indicator = "NOT_REAL", Operator = ">", Value = 0m },
                    new EntryRule { Indicator = "RSI", Operator = "<", Value = 30m, Params = new() { ["period"] = 0m } }
                ]
            }
        });

        var errors = CustomPatternValidator.Validate(pattern);

        errors.Should().Contain(error => error.Contains("지원하지 않는 지표"));
        errors.Should().Contain(error => error.Contains("0보다 커야"));
    }

    [Fact]
    public void Validate_IgnoresDisabledWeightTiers()
    {
        var pattern = ValidPattern();
        pattern.UseWeightTiers = false;
        pattern.WeightTiersJson = JsonSerializer.Serialize(new[]
        {
            new WeightTier { Label = "사용 안 함", Conditions = [], AllocationPercent = 200m }
        });

        CustomPatternValidator.Validate(pattern).Should().BeEmpty();
    }

    private static CustomPatternDefinition ValidPattern() => new()
    {
        Name = "검증 전략",
        EntryGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup { Label = "진입", Logic = "AND", Rules = [Rule()] }
        })
    };

    private static EntryRule Rule() => new()
    {
        Indicator = "RSI",
        Operator = "<=",
        Value = 30m,
        Params = new() { ["period"] = 14m }
    };
}

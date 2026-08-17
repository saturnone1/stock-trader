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

using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LiveStrategyCompatibilityTests
{
    [Fact]
    public void PartialExitInfrastructureDoesNotEnableLivePartialProfitStrategies()
    {
        var compilation = StrategyCompiler.Compile(new StrategyDocument
        {
            Name = "부분 익절 전략",
            PartialProfitR = 1.5m,
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "RSI",
                    Operator = "<=",
                    Value = 30m,
                    Params = new() { ["period"] = 14m },
                },
            }),
        });

        compilation.IsValid.Should().BeTrue();
        LiveStrategyCompatibilityPolicy.SupportsPartialExit.Should().BeFalse();
        LiveStrategyCompatibilityPolicy.Validate(compilation.Strategy!)
            .Should().ContainSingle(error => error.Contains("부분 익절"));
    }
}

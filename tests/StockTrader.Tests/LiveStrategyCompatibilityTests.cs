using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LiveStrategyCompatibilityTests
{
    [Fact]
    public void PartialProfitStrategyIsSupportedWhenOtherLiveConstraintsAreMet()
    {
        var compilation = StrategyCompiler.Compile(new StrategyDocument
        {
            Name = "부분 익절 전략",
            PartialProfitR = 1.5m,
            EntryMode = StrategyCatalog.NextOpenEntryMode,
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
        LiveStrategyCompatibilityPolicy.SupportsPartialExit.Should().BeTrue();
        LiveStrategyCompatibilityPolicy.Validate(compilation.Strategy!)
            .Should().BeEmpty();
    }
}

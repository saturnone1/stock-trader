using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class StrategyCompilerTests
{
    [Fact]
    public void Compile_ParsesAllRuntimeSettingsAndCollectsReferenceSymbolsOnce()
    {
        var pattern = ValidPattern();
        pattern.EntryGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup { Rules = [Rule(" spy "), Rule("QQQ")] }
        });
        pattern.ExitRulesJson = JsonSerializer.Serialize(new[] { Rule("SPY") });
        pattern.CircuitBreakerJson = JsonSerializer.Serialize(new CircuitBreakerConfig { ConsecutiveLossLimit = 3 });
        pattern.ReentryJson = JsonSerializer.Serialize(new ReentryConfig { CooldownBarsAfterLoss = 2 });
        pattern.PortfolioRulesJson = JsonSerializer.Serialize(new PortfolioRulesConfig { MaxTotalPositions = 5 });

        var result = StrategyCompiler.Compile(pattern);

        result.IsValid.Should().BeTrue();
        result.Strategy!.SchemaVersion.Should().Be(StrategyCompiler.CurrentSchemaVersion);
        result.Strategy.ReferenceSymbols.Should().Equal("QQQ", "SPY");
        result.Strategy.CircuitBreaker.ConsecutiveLossLimit.Should().Be(3);
        result.Strategy.Reentry.CooldownBarsAfterLoss.Should().Be(2);
        result.Strategy.PortfolioRules.MaxTotalPositions.Should().Be(5);
    }

    [Fact]
    public void Compile_ReturnsScopedErrorForMalformedJson()
    {
        var pattern = ValidPattern();
        pattern.EntryGroupsJson = "{broken";

        var result = StrategyCompiler.Compile(pattern);

        result.IsValid.Should().BeFalse();
        result.Strategy.Should().BeNull();
        result.Errors.Should().Contain(error => error.Contains("매수 상황 설정 형식"));
    }

    [Fact]
    public void Compile_NormalizesLegacyEmptyAtrExitToDefaultExecution()
    {
        var result = StrategyCompiler.Compile(ValidPattern());

        result.IsValid.Should().BeTrue();
        result.Strategy!.DynamicExit.Should().BeNull();
    }

    [Fact]
    public void Compile_AcceptsLegacyUnversionedDocumentThroughCompatibilityReader()
    {
        var pattern = ValidPattern();
        pattern.DocumentVersion = StrategyDocumentVersions.LegacyUnversioned;

        var result = StrategyCompiler.Compile(pattern);

        result.IsValid.Should().BeTrue();
        pattern.DocumentVersion.Should().Be(StrategyDocumentVersions.LegacyUnversioned,
            "compilation must not mutate a stored compatibility document");
    }

    [Fact]
    public void Compile_RejectsUnknownFutureDocumentInsteadOfGuessingItsMeaning()
    {
        var pattern = ValidPattern();
        pattern.DocumentVersion = StrategyDocumentVersions.Current + 1;

        var result = StrategyCompiler.Compile(pattern);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("지원하지 않는 전략 문서 버전"));
    }

    private static StrategyDocument ValidPattern() => new()
    {
        Name = "컴파일 전략",
        EntryGroupsJson = JsonSerializer.Serialize(new[] { new ConditionGroup { Rules = [Rule()] } })
    };

    private static EntryRule Rule(string? referenceSymbol = null) => new()
    {
        Indicator = "RSI",
        Operator = "<=",
        Value = 30m,
        RefSymbol = referenceSymbol,
        Params = new() { ["period"] = 14m }
    };
}

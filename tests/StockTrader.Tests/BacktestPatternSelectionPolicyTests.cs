using FluentAssertions;
using StockTrader.Application.Backtesting;

namespace StockTrader.Tests;

public sealed class BacktestPatternSelectionPolicyTests
{
    [Theory]
    [InlineData(PatternType.OpeningRangeBreakout)]
    [InlineData(PatternType.EarningsDrift)]
    public void UnavailableBuiltInPatternsFailClosedInsteadOfReturningAZeroTradeBacktest(
        PatternType patternType)
    {
        var errors = BacktestPatternSelectionPolicy.Validate([patternType], null);

        errors.Should().ContainSingle();
        errors[0].Should().Contain(PatternCatalog.DisplayName(patternType));
        errors[0].Should().Contain("실행할 수 없습니다");
    }

    [Fact]
    public void OperationalBuiltInPatternIsAccepted()
    {
        BacktestPatternSelectionPolicy.Validate([PatternType.Breakout], null)
            .Should().BeEmpty();
    }

    [Fact]
    public void UnknownPatternCodeFailsClosedWithAValidationError()
    {
        var errors = BacktestPatternSelectionPolicy.Validate([(PatternType)999], null);

        errors.Should().ContainSingle("*알 수 없는 전략 코드(999)*");
    }

    [Fact]
    public void CustomSelectionRequiresAnExecutionDocument()
    {
        BacktestPatternSelectionPolicy.Validate([PatternType.Custom], null)
            .Should().ContainSingle(error => error.Contains("전략 문서"));
    }
}

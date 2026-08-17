using StockTrader.Application.Strategies;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

/// <summary>기존 호출자를 위한 호환 계층. 실제 파싱과 검증은 StrategyCompiler가 담당한다.</summary>
public static class CustomPatternValidator
{
    public static IReadOnlyList<string> Validate(CustomPatternDefinition pattern) =>
        StrategyCompiler.Compile(pattern).Errors;
}

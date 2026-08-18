using StockTrader.Application.Strategies;

namespace StockTrader.Application.Backtesting;

public static class BacktestPatternSelectionPolicy
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<PatternType>? patterns,
        IReadOnlyList<StrategyDocument>? customPatterns)
    {
        if (patterns is null || patterns.Count == 0)
            return ["백테스트할 전략을 하나 이상 선택해야 합니다."];

        var errors = patterns
            .Where(pattern => pattern != PatternType.Custom
                && !PatternCatalog.IsOperationalBuiltIn(pattern))
            .Distinct()
            .Select(pattern =>
            {
                if (!PatternCatalog.TryGet(pattern, out var descriptor))
                    return $"알 수 없는 전략 코드({(int)pattern})입니다.";
                return $"{descriptor.DisplayName} 전략은 실행할 수 없습니다. {descriptor.UnavailableReason}";
            })
            .ToList();
        if (patterns.Contains(PatternType.Custom)
            && (customPatterns is null || customPatterns.Count == 0))
            errors.Add("사용자 전략 백테스트에는 전략 문서가 하나 이상 필요합니다.");
        return errors;
    }
}

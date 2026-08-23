using StockTrader.Domain.Strategies;

namespace StockTrader.Engine.Rules;

public delegate (decimal current, decimal previous) RuleIndicatorCalculator(
    RuleIndicatorParameters parameters,
    RuleIndicatorEvaluationContext context,
    int currentIndex,
    int previousIndex);

/// <summary>
/// 중앙 지표 카탈로그의 각 코드를 정확히 하나의 계산 구현에 연결한다.
/// </summary>
public static class RuleIndicatorCalculatorRegistry
{
    private static readonly IReadOnlyDictionary<string, RuleIndicatorCalculator> Calculators = Build();

    public static IReadOnlyCollection<string> Codes { get; } = Calculators.Keys.ToArray();

    public static bool TryGet(string indicator, out RuleIndicatorCalculator calculator) =>
        Calculators.TryGetValue(indicator, out calculator!);

    private static IReadOnlyDictionary<string, RuleIndicatorCalculator> Build()
    {
        var calculators = new Dictionary<string, RuleIndicatorCalculator>(StringComparer.OrdinalIgnoreCase);
        Register(calculators, StandardRuleIndicatorCalculators.All);
        Register(calculators, PriceStructureRuleIndicatorCalculators.All);
        Register(calculators, MomentumVolumeRuleIndicatorCalculators.All);

        var missing = IndicatorCatalog.All
            .Select(descriptor => descriptor.Code)
            .Where(code => !calculators.ContainsKey(code))
            .ToArray();
        var unknown = calculators.Keys
            .Where(code => !IndicatorCatalog.Contains(code))
            .ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"지표 계산 레지스트리가 중앙 카탈로그와 일치하지 않습니다. " +
                $"missing=[{string.Join(',', missing)}], unknown=[{string.Join(',', unknown)}]");
        }

        return calculators;
    }

    private static void Register(
        IDictionary<string, RuleIndicatorCalculator> target,
        IReadOnlyDictionary<string, RuleIndicatorCalculator> source)
    {
        foreach (var (code, calculator) in source)
        {
            if (!target.TryAdd(code, calculator))
                throw new InvalidOperationException($"중복 지표 계산 코드입니다: {code}");
        }
    }
}

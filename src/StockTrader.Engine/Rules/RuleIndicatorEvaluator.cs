using StockTrader.Engine.MarketData;
using StockTrader.Engine.Indicators;

namespace StockTrader.Engine.Rules;

/// <summary>
/// 중앙 지표 계산 레지스트리를 호출하고 한 평가 주기의 캐시 컨텍스트를 만든다.
/// 규칙 결합과 진입·청산 정책은 소유하지 않는다.
/// </summary>
public sealed class RuleIndicatorEvaluator
{
    private readonly IndicatorCalculator _indicators;

    public RuleIndicatorEvaluator(IndicatorCalculator? indicators = null)
    {
        _indicators = indicators ?? new IndicatorCalculator();
    }

    public RuleIndicatorEvaluationContext CreateContext(PriceBar[] bars) =>
        new(bars, _indicators);

    /// <summary>
    /// offset=0은 현재 봉, offset=1은 한 봉 전이다.
    /// 반환값은 crosses 연산에 필요한 선택 봉과 그 이전 봉의 값이다.
    /// </summary>
    public (decimal current, decimal prev) Compute(
        string indicator,
        Dictionary<string, decimal> parameters,
        RuleIndicatorEvaluationContext context,
        int offset)
    {
        var currentIndex = context.Bars.Length - 1 - offset;
        var previousIndex = context.Bars.Length - 2 - offset;
        if (currentIndex < 2 || previousIndex < 1) return (0, 0);
        if (!RuleIndicatorCalculatorRegistry.TryGet(indicator, out var calculator)) return (0, 0);

        return calculator(
            new RuleIndicatorParameters(indicator, parameters),
            context,
            currentIndex,
            previousIndex);
    }
}

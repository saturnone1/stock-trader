using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using EvalContext = StockTrader.Services.Patterns.RuleIndicatorEvaluationContext;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Evaluates one compiled strategy condition against an immutable bar snapshot.
/// Reference data and its as-of boundary are explicit so historical evaluation cannot
/// accidentally observe bars from the future.
/// </summary>
internal sealed class RuleConditionEvaluator
{
    private readonly RuleIndicatorEvaluator _indicators;

    public RuleConditionEvaluator(RuleIndicatorEvaluator indicators)
    {
        _indicators = indicators;
    }

    public RuleConditionResult Evaluate(
        EntryRule rule,
        EvalContext context,
        IReadOnlyDictionary<string, OhlcvBar[]>? referenceData = null,
        DateTime? referenceAsOf = null)
    {
        try
        {
            var evaluationContext = context;
            var referencePrefix = "";
            if (!string.IsNullOrWhiteSpace(rule.RefSymbol))
            {
                var referenceSymbol = rule.RefSymbol.ToUpperInvariant();
                if (referenceData == null || !referenceData.TryGetValue(referenceSymbol, out var referenceBars))
                    return RuleConditionResult.Failed($"{rule.RefSymbol}: 참조 데이터 없음");

                var availableBars = referenceAsOf.HasValue
                    ? referenceBars.Where(bar => bar.Timestamp <= referenceAsOf.Value).ToArray()
                    : referenceBars;
                if (availableBars.Length < StrategyEvaluationPolicy.MinimumWarmupBars)
                    return RuleConditionResult.Failed($"{rule.RefSymbol}: 참조 데이터 부족");

                evaluationContext = _indicators.CreateContext(availableBars);
                referencePrefix = $"{rule.RefSymbol}:";
            }

            decimal GetThreshold(int offset, out decimal previousThreshold)
            {
                if (!string.IsNullOrEmpty(rule.CompareIndicator))
                {
                    var compared = _indicators.Compute(
                        rule.CompareIndicator,
                        rule.CompareParams ?? new Dictionary<string, decimal>(),
                        evaluationContext,
                        offset);
                    previousThreshold = compared.prev;
                    return compared.current;
                }

                previousThreshold = rule.Value;
                return rule.Value;
            }

            var thresholdLabel = !string.IsNullOrEmpty(rule.CompareIndicator)
                ? rule.CompareIndicator
                : $"{rule.Value}";

            if (rule.ConsecutiveBars > 1)
            {
                for (var offset = 0; offset < rule.ConsecutiveBars; offset++)
                {
                    if (!HasSufficientHistory(rule, evaluationContext, offset))
                        return RuleConditionResult.Failed(
                            $"{referencePrefix}{rule.Indicator} insufficient history for {rule.ConsecutiveBars} bars");

                    var value = _indicators.Compute(rule.Indicator, rule.Params, evaluationContext, offset);
                    var threshold = GetThreshold(offset, out var previousThreshold);
                    if (!Compare(value.current, value.prev, rule.Operator, threshold, previousThreshold))
                        return RuleConditionResult.Failed(
                            $"{referencePrefix}{rule.Indicator} not held {rule.ConsecutiveBars} bars");
                }

                var latest = _indicators.Compute(rule.Indicator, rule.Params, evaluationContext, 0);
                return RuleConditionResult.Passed(
                    $"{referencePrefix}{rule.Indicator}={latest.current:F2} held {rule.ConsecutiveBars} bars");
            }

            if (rule.WithinBars > 0)
            {
                var checkedBars = 0;
                for (var offset = 0; offset < rule.WithinBars; offset++)
                {
                    if (!HasSufficientHistory(rule, evaluationContext, offset))
                        break;

                    checkedBars++;
                    var value = _indicators.Compute(rule.Indicator, rule.Params, evaluationContext, offset);
                    var threshold = GetThreshold(offset, out var previousThreshold);
                    if (Compare(value.current, value.prev, rule.Operator, threshold, previousThreshold))
                        return RuleConditionResult.Passed(
                            $"{referencePrefix}{rule.Indicator}={value.current:F2} {rule.Operator} {thresholdLabel} (within {rule.WithinBars})");
                }

                return RuleConditionResult.Failed(checkedBars == 0
                    ? $"{referencePrefix}{rule.Indicator} insufficient history for within {rule.WithinBars}"
                    : $"{referencePrefix}{rule.Indicator} not met within {rule.WithinBars} bars");
            }

            if (!HasSufficientHistory(rule, evaluationContext, 0))
                return RuleConditionResult.Failed($"{referencePrefix}{rule.Indicator} insufficient history");

            var current = _indicators.Compute(rule.Indicator, rule.Params, evaluationContext, 0);
            var currentThreshold = GetThreshold(0, out var previousCurrentThreshold);
            var passed = Compare(
                current.current,
                current.prev,
                rule.Operator,
                currentThreshold,
                previousCurrentThreshold);
            var details = $"{referencePrefix}{rule.Indicator}={current.current:F2} {rule.Operator} {thresholdLabel}";
            return new RuleConditionResult(passed, details);
        }
        catch
        {
            return RuleConditionResult.Failed($"{rule.Indicator}: 평가 실패");
        }
    }

    internal static bool Compare(
        decimal current,
        decimal previous,
        string comparisonOperator,
        decimal threshold,
        decimal previousThreshold) => comparisonOperator switch
    {
        ">" => current > threshold,
        "<" => current < threshold,
        ">=" => current >= threshold,
        "<=" => current <= threshold,
        "crosses_above" => previous <= previousThreshold && current > threshold,
        "crosses_below" => previous >= previousThreshold && current < threshold,
        _ => false
    };

    private static bool HasSufficientHistory(EntryRule rule, EvalContext context, int offset)
    {
        var requiredBars = IndicatorCatalog.RequiredBars(rule.Indicator, rule.Params);
        if (!string.IsNullOrWhiteSpace(rule.CompareIndicator))
        {
            requiredBars = Math.Max(
                requiredBars,
                IndicatorCatalog.RequiredBars(rule.CompareIndicator, rule.CompareParams));
        }

        return context.Bars.Length - offset >= requiredBars;
    }
}

internal readonly record struct RuleConditionResult(bool IsMatch, string Details)
{
    public static RuleConditionResult Passed(string details) => new(true, details);
    public static RuleConditionResult Failed(string details) => new(false, details);
}

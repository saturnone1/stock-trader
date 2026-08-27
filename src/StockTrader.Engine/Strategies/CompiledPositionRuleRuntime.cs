using StockTrader.Application.Strategies;
using StockTrader.Engine.MarketData;
using StockTrader.Engine.Rules;
using StockTrader.Models;

namespace StockTrader.Engine.Strategies;

/// <summary>
/// Deterministic exit/scaling rule evaluator shared by preview, backtest, Edge compatibility,
/// and the Trading Core service. It deliberately accepts only immutable compiled rules and bars.
/// </summary>
public sealed class CompiledPositionRuleRuntime
{
    private readonly CompiledStrategy _strategy;
    private readonly RuleIndicatorEvaluator _indicators = new();
    private readonly RuleConditionEvaluator _conditions;
    private readonly RuleGroupEvaluator _groups;

    public CompiledPositionRuleRuntime(CompiledStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _conditions = new RuleConditionEvaluator(_indicators);
        _groups = new RuleGroupEvaluator(_conditions);
    }

    public bool HasExitRules => _strategy.ExitGroups.Count > 0 || _strategy.ExitRules.Count > 0;
    public bool HasScalingRules => _strategy.ScalingRules.Count > 0;

    public bool ShouldExit(
        PriceBar[] bars,
        IReadOnlyDictionary<string, PriceBar[]>? referenceData = null,
        DateTime? referenceAsOf = null)
    {
        if (bars.Length == 0 || !HasExitRules) return false;
        var context = _indicators.CreateContext(bars);
        if (_strategy.ExitGroups.Count > 0)
            return _groups.Evaluate(
                _strategy.ExitGroups, _strategy.Source.ExitGroupsLogic,
                context, referenceData, referenceAsOf).IsMatch;

        var useOr = string.Equals(
            _strategy.Source.ExitRulesLogic, "OR", StringComparison.OrdinalIgnoreCase);
        foreach (var rule in _strategy.ExitRules)
        {
            var matched = _conditions.Evaluate(
                rule, context, referenceData, referenceAsOf).IsMatch;
            if (useOr && matched) return true;
            if (!useOr && !matched) return false;
        }
        return !useOr;
    }

    public (int RuleIndex, ScalingRule Rule)? EvaluateScaling(
        PriceBar[] bars,
        decimal currentProfitPercent,
        IReadOnlyDictionary<int, int> executionCounts,
        IReadOnlyDictionary<string, PriceBar[]>? referenceData = null,
        DateTime? referenceAsOf = null)
    {
        if (bars.Length == 0) return null;
        var context = _indicators.CreateContext(bars);
        for (var index = 0; index < _strategy.ScalingRules.Count; index++)
        {
            var rule = _strategy.ScalingRules[index];
            executionCounts.TryGetValue(index, out var count);
            if (count >= rule.MaxCount || currentProfitPercent < rule.MinProfitPercent
                || rule.Conditions.Count == 0)
                continue;
            var useAnd = string.Equals(rule.Logic, "AND", StringComparison.OrdinalIgnoreCase);
            var results = rule.Conditions.Select(condition => _conditions.Evaluate(
                condition, context, referenceData, referenceAsOf).IsMatch);
            if (useAnd ? results.All(value => value) : results.Any(value => value))
                return (index, rule);
        }
        return null;
    }
}

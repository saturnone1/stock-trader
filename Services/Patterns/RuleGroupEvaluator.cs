using StockTrader.Domain.MarketData;
using StockTrader.Models;
using EvalContext = StockTrader.Services.Patterns.RuleIndicatorEvaluationContext;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Combines condition results using the strategy's inner- and outer-group logic.
/// Weight accounting is kept here so entry confidence has one deterministic owner.
/// </summary>
internal sealed class RuleGroupEvaluator
{
    private readonly RuleConditionEvaluator _conditions;

    public RuleGroupEvaluator(RuleConditionEvaluator conditions)
    {
        _conditions = conditions;
    }

    public RuleGroupResult Evaluate(
        IReadOnlyList<ConditionGroup> groups,
        string groupsLogic,
        EvalContext context,
        IReadOnlyDictionary<string, OhlcvBar[]>? referenceData = null,
        DateTime? referenceAsOf = null)
    {
        var combineGroupsWithAnd = string.Equals(groupsLogic, "AND", StringComparison.OrdinalIgnoreCase);
        var groupMatches = new List<bool>();
        var matchedDetails = new List<string>();
        decimal totalWeight = 0;
        decimal matchedWeight = 0;

        foreach (var group in groups)
        {
            if (group.Rules.Count == 0)
                continue;

            var combineRulesWithAnd = string.Equals(group.Logic, "AND", StringComparison.OrdinalIgnoreCase);
            var results = group.Rules
                .Select(rule => (rule, result: _conditions.Evaluate(
                    rule,
                    context,
                    referenceData,
                    referenceAsOf)))
                .ToArray();

            var groupMatched = combineRulesWithAnd
                ? results.All(item => item.result.IsMatch)
                : results.Any(item => item.result.IsMatch);
            groupMatches.Add(groupMatched);

            totalWeight += results.Sum(item => item.rule.Weight);
            matchedWeight += results.Where(item => item.result.IsMatch).Sum(item => item.rule.Weight);

            if (groupMatched)
            {
                var label = string.IsNullOrEmpty(group.Label) ? "" : $"[{group.Label}] ";
                matchedDetails.Add(label + string.Join(", ", results
                    .Where(item => item.result.IsMatch)
                    .Select(item => item.result.Details)));
            }
        }

        var matched = combineGroupsWithAnd
            ? groupMatches.Count > 0 && groupMatches.All(value => value)
            : groupMatches.Any(value => value);

        return new RuleGroupResult(
            matched,
            matchedWeight,
            totalWeight,
            string.Join(" | ", matchedDetails));
    }
}

internal readonly record struct RuleGroupResult(
    bool IsMatch,
    decimal MatchedWeight,
    decimal TotalWeight,
    string Details);

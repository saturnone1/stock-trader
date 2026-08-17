using System.Text.Json;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Optimization;

/// <summary>저장 전략을 오염시키지 않고 최적화 후보 전략을 생성한다.</summary>
public static class StrategyVariantFactory
{
    /// <summary>
    /// 패턴 정의를 얕은 복사(JSON 필드는 문자열 복사)합니다.
    /// 최적화 루프에서 basePattern을 오염시키지 않기 위해 사용합니다.
    /// </summary>
    public static StrategyDocument CloneStrategyDocument(StrategyDocument src)
    {
        ArgumentNullException.ThrowIfNull(src);
        return src.Copy();
    }

    /// <summary>
    /// 파라미터 스냅샷을 패턴 정의에 적용합니다.
    /// null인 필드는 기존 값을 유지합니다.
    /// JSON 필드(CircuitBreaker, Reentry, PortfolioRules)는 파싱 후 필드를 수정하여 재직렬화합니다.
    /// </summary>
    public static void ApplyOptimizeOverrides(StrategyDocument pattern, OptimizeParamSnapshot snap)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        static string NormalizeRuleScope(string? scope) =>
        string.Equals(scope, "Exit", StringComparison.OrdinalIgnoreCase) ? "Exit" : "Entry";

        static List<EntryRule>? GetOverrideTargets(StrategyDocument pattern, JsonSerializerOptions jsonOpts, out bool fromGroups)
        {
            try
            {
                var groups = JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.EntryGroupsJson, jsonOpts);
                if (groups is { Count: > 0 })
                {
                    fromGroups = true;
                    return groups.SelectMany(group => group.Rules).ToList();
                }
            }
            catch
            {
                // group parsing failed, fall through to flat rules
            }

            try
            {
                fromGroups = false;
                return JsonSerializer.Deserialize<List<EntryRule>>(pattern.EntryRulesJson, jsonOpts);
            }
            catch
            {
                fromGroups = false;
                return null;
            }
        }

        static void SaveOverrideTargets(StrategyDocument pattern, JsonSerializerOptions jsonOpts, bool fromGroups, List<EntryRule> flattenedRules)
        {
            if (fromGroups)
            {
                try
                {
                    var groups = JsonSerializer.Deserialize<List<ConditionGroup>>(pattern.EntryGroupsJson, jsonOpts);
                    if (groups is { Count: > 0 })
                    {
                        var index = 0;
                        foreach (var group in groups)
                        {
                            for (var i = 0; i < group.Rules.Count && index < flattenedRules.Count; i++, index++)
                                group.Rules[i] = flattenedRules[index];
                        }

                        pattern.EntryGroupsJson = JsonSerializer.Serialize(groups);
                    }
                }
                catch
                {
                    // keep original group JSON on failure
                }

                return;
            }

            pattern.EntryRulesJson = JsonSerializer.Serialize(flattenedRules);
        }

        static List<EntryRule>? GetExitOverrideTargets(StrategyDocument pattern, JsonSerializerOptions jsonOpts)
        {
            try
            {
                return JsonSerializer.Deserialize<List<EntryRule>>(pattern.ExitRulesJson, jsonOpts);
            }
            catch
            {
                return null;
            }
        }

        static void SaveExitOverrideTargets(StrategyDocument pattern, List<EntryRule> rules)
        {
            pattern.ExitRulesJson = JsonSerializer.Serialize(rules);
        }

        // ── 기존 숫자형 파라미터 ──
        if (snap.AtrStopMultiplier.HasValue) pattern.AtrStopMultiplier = snap.AtrStopMultiplier.Value;
        if (snap.AtrTargetMultiplier.HasValue) pattern.AtrTargetMultiplier = snap.AtrTargetMultiplier.Value;
        if (snap.MaxHoldingBars.HasValue) pattern.MaxHoldingBars = snap.MaxHoldingBars.Value;
        if (snap.TrailingAtr.HasValue) pattern.TrailingAtr = snap.TrailingAtr.Value;
        if (snap.PartialProfitR.HasValue) pattern.PartialProfitR = snap.PartialProfitR.Value;

        // ── 카테고리형 파라미터 ──
        if (snap.EntryLogic != null) pattern.EntryLogic = snap.EntryLogic;
        if (snap.RequireBullRegime.HasValue) pattern.RequireBullRegime = snap.RequireBullRegime.Value;
        if (snap.EntryMode != null) pattern.EntryMode = snap.EntryMode;
        if (snap.SizingMode != null) pattern.SizingMode = snap.SizingMode;
        if (snap.ExitLogic != null) pattern.ExitRulesLogic = snap.ExitLogic;
        if (snap.TimeFrame.HasValue && Enum.IsDefined((TimeFrame)snap.TimeFrame.Value))
            pattern.TimeFrame = (TimeFrame)snap.TimeFrame.Value;

        // ── 기본 비중 ──
        if (snap.DefaultAllocationPercent.HasValue)
            pattern.DefaultAllocationPercent = snap.DefaultAllocationPercent.Value;

        // ── CircuitBreakerJson 파싱 → 수정 → 재직렬화 ──
        if (snap.CircuitBreakerConsecutiveLossLimit.HasValue
        || snap.CircuitBreakerCooldownBars.HasValue
        || snap.CircuitBreakerMaxDrawdownPercent.HasValue)
        {
            try
            {
                var cb = JsonSerializer.Deserialize<CircuitBreakerConfig>(pattern.CircuitBreakerJson, jsonOpts) ?? new();
                if (snap.CircuitBreakerConsecutiveLossLimit.HasValue) cb.ConsecutiveLossLimit = snap.CircuitBreakerConsecutiveLossLimit.Value;
                if (snap.CircuitBreakerCooldownBars.HasValue) cb.CooldownBars = snap.CircuitBreakerCooldownBars.Value;
                if (snap.CircuitBreakerMaxDrawdownPercent.HasValue) cb.MaxDrawdownPercent = snap.CircuitBreakerMaxDrawdownPercent.Value;
                pattern.CircuitBreakerJson = JsonSerializer.Serialize(cb);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── ReentryJson 파싱 → 수정 → 재직렬화 ──
        if (snap.ReentryCooldownAfterLoss.HasValue || snap.ReentryCooldownAfterWin.HasValue)
        {
            try
            {
                var rc = JsonSerializer.Deserialize<ReentryConfig>(pattern.ReentryJson, jsonOpts) ?? new();
                if (snap.ReentryCooldownAfterLoss.HasValue) rc.CooldownBarsAfterLoss = snap.ReentryCooldownAfterLoss.Value;
                if (snap.ReentryCooldownAfterWin.HasValue) rc.CooldownBarsAfterWin = snap.ReentryCooldownAfterWin.Value;
                pattern.ReentryJson = JsonSerializer.Serialize(rc);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── PortfolioRulesJson 파싱 → 수정 → 재직렬화 ──
        if (snap.PortfolioMaxPositions.HasValue
        || snap.PortfolioMaxSinglePercent.HasValue
        || snap.PortfolioMaxEntriesPerDay.HasValue)
        {
            try
            {
                var pr = JsonSerializer.Deserialize<PortfolioRulesConfig>(pattern.PortfolioRulesJson, jsonOpts) ?? new();
                if (snap.PortfolioMaxPositions.HasValue) pr.MaxTotalPositions = snap.PortfolioMaxPositions.Value;
                if (snap.PortfolioMaxSinglePercent.HasValue) pr.MaxSinglePositionPercent = snap.PortfolioMaxSinglePercent.Value;
                if (snap.PortfolioMaxEntriesPerDay.HasValue) pr.MaxEntriesPerDay = snap.PortfolioMaxEntriesPerDay.Value;
                pattern.PortfolioRulesJson = JsonSerializer.Serialize(pr);
            }
            catch { /* JSON 파싱 실패 시 기존 값 유지 */ }
        }

        // ── RuleParamOverrides / RuleFieldOverrides: 활성 진입/청산 규칙 수정 → 재직렬화 ──
        if (snap.RuleOverrides.Count > 0)
        {
            foreach (var scopeGroup in snap.RuleOverrides.GroupBy(entry => NormalizeRuleScope(entry.Scope)))
            {
                try
                {
                    if (scopeGroup.Key == "Exit")
                    {
                        var rules = GetExitOverrideTargets(pattern, jsonOpts);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var paramKey = entry.ParamKey ?? string.Empty;
                            if (paramKey.StartsWith("compare.", StringComparison.OrdinalIgnoreCase))
                            {
                                var compareKey = paramKey["compare.".Length..];
                                rules[entry.RuleIndex].CompareParams[compareKey] = entry.Value;
                            }
                            else
                            {
                                rules[entry.RuleIndex].Params[paramKey] = entry.Value;
                            }
                        }
                        SaveExitOverrideTargets(pattern, rules);
                    }
                    else
                    {
                        var rules = GetOverrideTargets(pattern, jsonOpts, out var fromGroups);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var paramKey = entry.ParamKey ?? string.Empty;
                            if (paramKey.StartsWith("compare.", StringComparison.OrdinalIgnoreCase))
                            {
                                var compareKey = paramKey["compare.".Length..];
                                rules[entry.RuleIndex].CompareParams[compareKey] = entry.Value;
                            }
                            else
                            {
                                rules[entry.RuleIndex].Params[paramKey] = entry.Value;
                            }
                        }
                        SaveOverrideTargets(pattern, jsonOpts, fromGroups, rules);
                    }
                }
                catch { /* JSON 파싱 실패 시 룰 오버라이드 없이 진행 */ }
            }
        }

        if (snap.RuleFieldOverrides is { Count: > 0 })
        {
            foreach (var scopeGroup in snap.RuleFieldOverrides.GroupBy(entry => NormalizeRuleScope(entry.Scope)))
            {
                try
                {
                    if (scopeGroup.Key == "Exit")
                    {
                        var rules = GetExitOverrideTargets(pattern, jsonOpts);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var rule = rules[entry.RuleIndex];
                            switch (entry.FieldName.ToLowerInvariant())
                            {
                                case "value" when entry.NumericValue.HasValue:
                                    rule.Value = entry.NumericValue.Value; break;
                                case "withinbars" when entry.NumericValue.HasValue:
                                    rule.WithinBars = (int)entry.NumericValue.Value; break;
                                case "weight" when entry.NumericValue.HasValue:
                                    rule.Weight = entry.NumericValue.Value; break;
                                case "consecutivebars" when entry.NumericValue.HasValue:
                                    rule.ConsecutiveBars = (int)entry.NumericValue.Value; break;
                                case "operator" when entry.StringValue != null:
                                    rule.Operator = entry.StringValue; break;
                                case "compareindicator" when entry.StringValue != null:
                                    rule.CompareIndicator = entry.StringValue; break;
                            }
                        }
                        SaveExitOverrideTargets(pattern, rules);
                    }
                    else
                    {
                        var rules = GetOverrideTargets(pattern, jsonOpts, out var fromGroups);
                        if (rules == null) continue;
                        foreach (var entry in scopeGroup)
                        {
                            if (entry.RuleIndex < 0 || entry.RuleIndex >= rules.Count) continue;
                            var rule = rules[entry.RuleIndex];
                            switch (entry.FieldName.ToLowerInvariant())
                            {
                                case "value" when entry.NumericValue.HasValue:
                                    rule.Value = entry.NumericValue.Value; break;
                                case "withinbars" when entry.NumericValue.HasValue:
                                    rule.WithinBars = (int)entry.NumericValue.Value; break;
                                case "weight" when entry.NumericValue.HasValue:
                                    rule.Weight = entry.NumericValue.Value; break;
                                case "consecutivebars" when entry.NumericValue.HasValue:
                                    rule.ConsecutiveBars = (int)entry.NumericValue.Value; break;
                                case "operator" when entry.StringValue != null:
                                    rule.Operator = entry.StringValue; break;
                                case "compareindicator" when entry.StringValue != null:
                                    rule.CompareIndicator = entry.StringValue; break;
                            }
                        }
                        SaveOverrideTargets(pattern, jsonOpts, fromGroups, rules);
                    }
                }
                catch { /* JSON 파싱 실패 시 필드 오버라이드 없이 진행 */ }
            }
        }
    }
}

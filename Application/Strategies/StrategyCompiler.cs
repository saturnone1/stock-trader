using System.Text.Json;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Application.Strategies;

/// <summary>저장용 JSON 전략을 검증된 실행 모델로 한 번만 변환한다.</summary>
public static class StrategyCompiler
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> PositiveParameterKeys = new(StringComparer.OrdinalIgnoreCase)
        { "period", "cumulativePeriod", "bars", "lookback", "stddev", "smooth", "slow", "fast", "signal" };

    public static StrategyCompilationResult Compile(CustomPatternDefinition pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(pattern.Name)) errors.Add("전략 이름을 입력하세요.");
        if (pattern.AtrStopMultiplier <= 0) errors.Add("ATR 손절 배수는 0보다 커야 합니다.");
        if (pattern.AtrTargetMultiplier <= 0) errors.Add("ATR 목표 배수는 0보다 커야 합니다.");
        if (pattern.MaxHoldingBars < 0) errors.Add("최대 보유 봉 수는 0 이상이어야 합니다.");
        if (pattern.TrailingAtr < 0) errors.Add("트레일링 ATR은 0 이상이어야 합니다.");
        if (pattern.PartialProfitR < 0) errors.Add("부분 익절 R은 0 이상이어야 합니다.");
        if (pattern.DefaultAllocationPercent is < 0 or > 100) errors.Add("기본 매수 비중은 0~100%여야 합니다.");
        if (!StrategyCatalog.IsEntryMode(pattern.EntryMode)) errors.Add("매수 체결 시점이 올바르지 않습니다.");
        if (!Enum.IsDefined(pattern.TimeFrame)) errors.Add("전략 기준 봉이 올바르지 않습니다.");
        if (!StrategyCatalog.IsSizingMode(pattern.SizingMode)) errors.Add("주문 금액 계산법이 올바르지 않습니다.");
        ValidateLogic(pattern.EntryLogic, "기존 매수 조건", errors);
        ValidateLogic(pattern.EntryGroupsLogic, "매수 상황 결합", errors);
        ValidateLogic(pattern.ExitRulesLogic, "기존 매도 조건", errors);
        ValidateLogic(pattern.ExitGroupsLogic, "매도 상황 결합", errors);

        var entryGroups = ParseList<ConditionGroup>(pattern.EntryGroupsJson, "매수 상황", errors);
        var entryRules = ParseList<EntryRule>(pattern.EntryRulesJson, "기존 매수 조건", errors);
        var exitGroups = ParseList<ConditionGroup>(pattern.ExitGroupsJson, "매도 상황", errors);
        var exitRules = ParseList<EntryRule>(pattern.ExitRulesJson, "기존 매도 조건", errors);
        if (entryGroups.Count == 0 && entryRules.Count == 0) errors.Add("매수 상황이 최소 1개는 필요합니다.");
        ValidateGroups(entryGroups, "매수 상황", errors);
        ValidateGroups(exitGroups, "매도 상황", errors);
        ValidateRules(entryRules, "기존 매수 조건", errors);
        ValidateRules(exitRules, "기존 매도 조건", errors);

        var tiers = ParseList<WeightTier>(pattern.WeightTiersJson, "매수 비중", errors);
        if (!pattern.UseWeightTiers) tiers = [];
        foreach (var (tier, index) in tiers.Select((value, index) => (value, index)))
        {
            ValidateLogic(tier.Logic, $"매수 비중 {index + 1}", errors);
            if (tier.AllocationPercent is < 0 or > 100) errors.Add($"매수 비중 {index + 1}은 0~100%여야 합니다.");
            if (tier.Conditions.Count == 0) errors.Add($"매수 비중 {index + 1}의 적용 조건이 비어 있습니다.");
            ValidateRules(tier.Conditions, $"매수 비중 {index + 1}", errors);
        }

        var scaling = ParseList<ScalingRule>(pattern.ScalingRulesJson, "추가 매수·분할 매도", errors);
        foreach (var (rule, index) in scaling.Select((value, index) => (value, index)))
        {
            ValidateLogic(rule.Logic, $"추가 매수·분할 매도 {index + 1}", errors);
            if (!StrategyCatalog.IsScalingDirection(rule.Direction)) errors.Add($"추가 매수·분할 매도 {index + 1}의 방향이 올바르지 않습니다.");
            if (rule.Percent is <= 0 or > 100) errors.Add($"추가 매수·분할 매도 {index + 1} 비율은 0 초과 100% 이하여야 합니다.");
            if (rule.MaxCount < 1) errors.Add($"추가 매수·분할 매도 {index + 1} 최대 횟수는 1 이상이어야 합니다.");
            if (rule.Conditions.Count == 0) errors.Add($"추가 매수·분할 매도 {index + 1}의 실행 조건이 비어 있습니다.");
            ValidateRules(rule.Conditions, $"추가 매수·분할 매도 {index + 1}", errors);
        }

        var time = ParseObject<TimeFilter>(pattern.TimeFilterJson, "매매 가능 시기", errors);
        if (time.AllowedDaysOfWeek.Any(day => day is < 0 or > 6)) errors.Add("허용 요일은 일요일 0부터 토요일 6 사이여야 합니다.");
        if (time.BlockedMonths.Any(month => month is < 1 or > 12)) errors.Add("차단 월은 1~12 사이여야 합니다.");
        var breaker = ParseObject<CircuitBreakerConfig>(pattern.CircuitBreakerJson, "손실 시 거래 중단", errors);
        if (breaker.ConsecutiveLossLimit < 0 || breaker.CooldownBars < 0) errors.Add("손실 횟수와 중단 봉 수는 0 이상이어야 합니다.");
        if (breaker.MaxDrawdownPercent is < 0 or > 100) errors.Add("최대 낙폭은 0~100%여야 합니다.");
        var reentry = ParseObject<ReentryConfig>(pattern.ReentryJson, "재매수 대기", errors);
        if (reentry.CooldownBarsAfterLoss < 0 || reentry.CooldownBarsAfterWin < 0) errors.Add("재매수 대기 봉 수는 0 이상이어야 합니다.");
        var portfolio = ParseObject<PortfolioRulesConfig>(pattern.PortfolioRulesJson, "보유 한도", errors);
        if (portfolio.MaxTotalPositions < 0 || portfolio.MaxEntriesPerDay < 0) errors.Add("보유 종목 수와 하루 매수 횟수는 0 이상이어야 합니다.");
        if (portfolio.MaxSinglePositionPercent is < 0 or > 100) errors.Add("한 종목 최대 비중은 0~100%여야 합니다.");
        if (portfolio.MaxCorrelation is < 0 or > 1) errors.Add("최대 상관계수는 0~1 사이여야 합니다.");
        var dynamicExit = ParseObject<DynamicExitConfig>(pattern.DynamicExitJson, "손절·목표가", errors);
        if (!StrategyCatalog.IsStopMethod(dynamicExit.StopType)) errors.Add("손절가 계산 방식이 올바르지 않습니다.");
        if (!StrategyCatalog.IsTargetMethod(dynamicExit.TargetType)) errors.Add("목표가 계산 방식이 올바르지 않습니다.");
        ValidatePositiveParams(dynamicExit.StopParams, "손절가", errors);
        ValidatePositiveParams(dynamicExit.TargetParams, "목표가", errors);
        DynamicExitConfig? normalizedExit = dynamicExit.StopType.Equals("ATR", StringComparison.OrdinalIgnoreCase)
            && dynamicExit.StopParams.Count == 0 && dynamicExit.TargetType.Equals("ATR", StringComparison.OrdinalIgnoreCase)
            && dynamicExit.TargetParams.Count == 0 ? null : dynamicExit;

        var symbols = AllRules(entryRules, entryGroups, exitRules, exitGroups, tiers, scaling)
            .Select(rule => rule.RefSymbol?.Trim().ToUpperInvariant()).Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var compiled = new CompiledStrategy(CurrentSchemaVersion, pattern, entryRules, entryGroups, exitRules, exitGroups,
            tiers, scaling, time, breaker, reentry, portfolio, normalizedExit, symbols);
        if (pattern.EnableLiveTrading) errors.AddRange(LiveStrategyCompatibilityPolicy.Validate(compiled));
        var uniqueErrors = errors.Distinct().ToArray();
        return new(uniqueErrors.Length == 0 ? compiled : null, uniqueErrors);
    }

    private static IEnumerable<EntryRule> AllRules(IReadOnlyList<EntryRule> entries, IReadOnlyList<ConditionGroup> entryGroups,
        IReadOnlyList<EntryRule> exits, IReadOnlyList<ConditionGroup> exitGroups, IReadOnlyList<WeightTier> tiers, IReadOnlyList<ScalingRule> scaling) =>
        entries.Concat(entryGroups.SelectMany(g => g.Rules)).Concat(exits).Concat(exitGroups.SelectMany(g => g.Rules))
            .Concat(tiers.SelectMany(t => t.Conditions)).Concat(scaling.SelectMany(s => s.Conditions));

    private static void ValidateGroups(IEnumerable<ConditionGroup> groups, string scope, List<string> errors)
    {
        foreach (var (group, index) in groups.Select((value, index) => (value, index)))
        {
            ValidateLogic(group.Logic, $"{scope} {index + 1}", errors);
            if (group.Rules.Count == 0) errors.Add($"{scope} {index + 1}의 조건이 비어 있습니다.");
            ValidateRules(group.Rules, $"{scope} {index + 1}", errors);
        }
    }

    private static void ValidateRules(IEnumerable<EntryRule> rules, string scope, List<string> errors)
    {
        foreach (var (rule, index) in rules.Select((value, index) => (value, index)))
        {
            var prefix = $"{scope} / 조건 {index + 1}";
            if (string.IsNullOrWhiteSpace(rule.Indicator)) errors.Add($"{prefix}: 지표를 선택하세요.");
            else if (!IndicatorCatalog.Contains(rule.Indicator)) errors.Add($"{prefix}: 지원하지 않는 지표입니다.");
            if (!string.IsNullOrWhiteSpace(rule.CompareIndicator) && !IndicatorCatalog.Contains(rule.CompareIndicator)) errors.Add($"{prefix}: 비교 지표가 올바르지 않습니다.");
            if (!RuleOperatorCatalog.Contains(rule.Operator)) errors.Add($"{prefix}: 비교 방식이 올바르지 않습니다.");
            if (rule.WithinBars < 0 || rule.ConsecutiveBars < 0) errors.Add($"{prefix}: 봉 수는 0 이상이어야 합니다.");
            if (rule.WithinBars > 0 && rule.ConsecutiveBars > 0) errors.Add($"{prefix}: 최근 N봉과 연속 봉은 동시에 사용할 수 없습니다.");
            if (rule.Weight <= 0) errors.Add($"{prefix}: 가중치는 0보다 커야 합니다.");
            if (rule.Params.Any(pair => pair.Value < 0)) errors.Add($"{prefix}: 지표 계산 기간은 음수일 수 없습니다.");
            ValidatePositiveParams(rule.Params, prefix, errors); ValidatePositiveParams(rule.CompareParams, $"{prefix} 비교 지표", errors);
            if (rule.Indicator.Equals("MACD_HIST", StringComparison.OrdinalIgnoreCase) && rule.Params.TryGetValue("fast", out var fast)
                && rule.Params.TryGetValue("slow", out var slow) && fast >= slow) errors.Add($"{prefix}: MACD 빠른 기간은 느린 기간보다 작아야 합니다.");
        }
    }

    private static void ValidatePositiveParams(Dictionary<string, decimal>? parameters, string scope, List<string> errors)
    {
        if (parameters is null) return;
        foreach (var (key, value) in parameters)
            if ((PositiveParameterKeys.Contains(key) || key is "multiplier" or "multiple" or "percent") && value <= 0)
                errors.Add($"{scope}: {key} 값은 0보다 커야 합니다.");
    }

    private static void ValidateLogic(string? logic, string scope, List<string> errors)
    { if (!StrategyCatalog.IsLogicMode(logic)) errors.Add($"{scope}: 조건 결합은 AND 또는 OR여야 합니다."); }

    private static List<T> ParseList<T>(string? json, string scope, List<string> errors) => Parse<T[]>(json, "[]", scope, errors)?.ToList() ?? [];
    private static T ParseObject<T>(string? json, string scope, List<string> errors) where T : new() => Parse<T>(json, "{}", scope, errors) ?? new();
    private static T? Parse<T>(string? json, string fallback, string scope, List<string> errors)
    {
        try { return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(json) ? fallback : json, JsonOptions); }
        catch (JsonException) { errors.Add($"{scope} 설정 형식이 올바르지 않습니다."); return default; }
    }
}

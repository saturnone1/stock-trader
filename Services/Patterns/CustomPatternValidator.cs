using System.Text.Json;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Patterns;

public static class CustomPatternValidator
{
    private static readonly HashSet<string> Logics = new(StringComparer.OrdinalIgnoreCase) { "AND", "OR" };
    private static readonly HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase)
        { ">", "<", ">=", "<=", "crosses_above", "crosses_below" };
    private static readonly HashSet<string> Indicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "RSI", "CUMULATIVE_RSI", "PRICE_VS_SMA", "PRICE_VS_EMA", "MACD_HIST", "BOLLINGER_POS",
        "VOLUME_RATIO", "PRICE_CHANGE", "ATR", "SMA_SLOPE", "CANDLE_BODY", "DIST_FROM_HIGH",
        "DIST_FROM_LOW", "GAP", "HIGHER_LOW", "LOWER_HIGH", "INSIDE_BAR", "ENGULFING",
        "BREAKOUT_HIGH", "BREAKOUT_LOW", "CONSECUTIVE_UP", "CONSECUTIVE_DOWN", "ADX",
        "STOCHASTIC_K", "STOCHASTIC_D", "ATR_PERCENT", "VOLATILITY_20D", "OBV",
        "PRICE_VS_VWAP", "OBV_SLOPE", "CCI", "ROC", "WILLIAMS_R", "CMF"
    };
    private static readonly HashSet<string> EntryModes = new(StringComparer.OrdinalIgnoreCase) { "CurrentClose", "NextOpen" };
    private static readonly HashSet<string> SizingModes = new(StringComparer.OrdinalIgnoreCase) { "FixedRisk", "Kelly", "HalfKelly" };
    private static readonly HashSet<string> StopTypes = new(StringComparer.OrdinalIgnoreCase)
        { "ATR", "BOLLINGER_LOWER", "SMA", "EMA", "PREV_LOW", "PERCENT" };
    private static readonly HashSet<string> TargetTypes = new(StringComparer.OrdinalIgnoreCase)
        { "ATR", "BOLLINGER_UPPER", "SMA", "EMA", "PREV_HIGH", "R_MULTIPLE", "PERCENT" };
    private static readonly HashSet<string> PositiveParameterKeys = new(StringComparer.OrdinalIgnoreCase)
        { "period", "cumulativePeriod", "bars", "lookback", "stddev", "smooth", "slow", "fast", "signal" };

    public static IReadOnlyList<string> Validate(CustomPatternDefinition pattern)
    {
        var errors = new List<string>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (string.IsNullOrWhiteSpace(pattern.Name)) errors.Add("전략 이름을 입력하세요.");
        if (pattern.AtrStopMultiplier <= 0) errors.Add("ATR 손절 배수는 0보다 커야 합니다.");
        if (pattern.AtrTargetMultiplier <= 0) errors.Add("ATR 목표 배수는 0보다 커야 합니다.");
        if (pattern.MaxHoldingBars < 0) errors.Add("최대 보유 봉 수는 0 이상이어야 합니다.");
        if (pattern.TrailingAtr < 0) errors.Add("트레일링 ATR은 0 이상이어야 합니다.");
        if (pattern.PartialProfitR < 0) errors.Add("부분 익절 R은 0 이상이어야 합니다.");
        if (pattern.DefaultAllocationPercent is < 0 or > 100) errors.Add("기본 매수 비중은 0~100%여야 합니다.");
        if (!EntryModes.Contains(pattern.EntryMode)) errors.Add("매수 체결 시점이 올바르지 않습니다.");
        if (!Enum.IsDefined(pattern.TimeFrame)) errors.Add("전략 기준 봉이 올바르지 않습니다.");
        if (!SizingModes.Contains(pattern.SizingMode)) errors.Add("주문 금액 계산법이 올바르지 않습니다.");
        ValidateLogic(pattern.EntryGroupsLogic, "매수 상황 결합", errors);
        ValidateLogic(pattern.ExitGroupsLogic, "매도 상황 결합", errors);

        var entryGroups = Parse<List<ConditionGroup>>(pattern.EntryGroupsJson, "[]", "매수 상황", errors, options) ?? [];
        var legacyEntries = Parse<List<EntryRule>>(pattern.EntryRulesJson, "[]", "기존 매수 조건", errors, options) ?? [];
        var exitGroups = Parse<List<ConditionGroup>>(pattern.ExitGroupsJson, "[]", "매도 상황", errors, options) ?? [];
        var legacyExits = Parse<List<EntryRule>>(pattern.ExitRulesJson, "[]", "기존 매도 조건", errors, options) ?? [];
        if (entryGroups.Count == 0 && legacyEntries.Count == 0)
            errors.Add("매수 상황이 최소 1개는 필요합니다.");

        ValidateGroups(entryGroups, "매수 상황", errors);
        ValidateGroups(exitGroups, "매도 상황", errors);
        ValidateRules(legacyEntries, "기존 매수 조건", errors);
        ValidateRules(legacyExits, "기존 매도 조건", errors);

        var tiers = Parse<List<WeightTier>>(pattern.WeightTiersJson, "[]", "매수 비중", errors, options) ?? [];
        if (!pattern.UseWeightTiers) tiers = [];
        foreach (var (tier, index) in tiers.Select((value, index) => (value, index)))
        {
            ValidateLogic(tier.Logic, $"매수 비중 {index + 1}", errors);
            if (tier.AllocationPercent is < 0 or > 100) errors.Add($"매수 비중 {index + 1}은 0~100%여야 합니다.");
            if (tier.Conditions.Count == 0) errors.Add($"매수 비중 {index + 1}의 적용 조건이 비어 있습니다.");
            ValidateRules(tier.Conditions, $"매수 비중 {index + 1}", errors);
        }

        var scaling = Parse<List<ScalingRule>>(pattern.ScalingRulesJson, "[]", "추가 매수·분할 매도", errors, options) ?? [];
        foreach (var (rule, index) in scaling.Select((value, index) => (value, index)))
        {
            ValidateLogic(rule.Logic, $"추가 매수·분할 매도 {index + 1}", errors);
            if (rule.Direction is not ("SCALE_IN" or "SCALE_OUT")) errors.Add($"추가 매수·분할 매도 {index + 1}의 방향이 올바르지 않습니다.");
            if (rule.Percent is <= 0 or > 100) errors.Add($"추가 매수·분할 매도 {index + 1} 비율은 0 초과 100% 이하여야 합니다.");
            if (rule.MaxCount < 1) errors.Add($"추가 매수·분할 매도 {index + 1} 최대 횟수는 1 이상이어야 합니다.");
            if (rule.Conditions.Count == 0) errors.Add($"추가 매수·분할 매도 {index + 1}의 실행 조건이 비어 있습니다.");
            ValidateRules(rule.Conditions, $"추가 매수·분할 매도 {index + 1}", errors);
        }

        var time = Parse<TimeFilter>(pattern.TimeFilterJson, "{}", "매매 가능 시기", errors, options) ?? new();
        if (time.AllowedDaysOfWeek.Any(day => day is < 0 or > 6)) errors.Add("허용 요일은 일요일 0부터 토요일 6 사이여야 합니다.");
        if (time.BlockedMonths.Any(month => month is < 1 or > 12)) errors.Add("차단 월은 1~12 사이여야 합니다.");

        var breaker = Parse<CircuitBreakerConfig>(pattern.CircuitBreakerJson, "{}", "손실 시 거래 중단", errors, options) ?? new();
        if (breaker.ConsecutiveLossLimit < 0 || breaker.CooldownBars < 0) errors.Add("손실 횟수와 중단 봉 수는 0 이상이어야 합니다.");
        if (breaker.MaxDrawdownPercent is < 0 or > 100) errors.Add("최대 낙폭은 0~100%여야 합니다.");

        var reentry = Parse<ReentryConfig>(pattern.ReentryJson, "{}", "재매수 대기", errors, options) ?? new();
        if (reentry.CooldownBarsAfterLoss < 0 || reentry.CooldownBarsAfterWin < 0) errors.Add("재매수 대기 봉 수는 0 이상이어야 합니다.");

        var portfolio = Parse<PortfolioRulesConfig>(pattern.PortfolioRulesJson, "{}", "보유 한도", errors, options) ?? new();
        if (portfolio.MaxTotalPositions < 0 || portfolio.MaxEntriesPerDay < 0) errors.Add("보유 종목 수와 하루 매수 횟수는 0 이상이어야 합니다.");
        if (portfolio.MaxSinglePositionPercent is < 0 or > 100) errors.Add("한 종목 최대 비중은 0~100%여야 합니다.");
        if (portfolio.MaxCorrelation is < 0 or > 1) errors.Add("최대 상관계수는 0~1 사이여야 합니다.");

        var dynamicExit = Parse<DynamicExitConfig>(pattern.DynamicExitJson, "{}", "손절·목표가", errors, options) ?? new();
        if (!StopTypes.Contains(dynamicExit.StopType)) errors.Add("손절가 계산 방식이 올바르지 않습니다.");
        if (!TargetTypes.Contains(dynamicExit.TargetType)) errors.Add("목표가 계산 방식이 올바르지 않습니다.");
        ValidatePositiveParams(dynamicExit.StopParams, "손절가", errors);
        ValidatePositiveParams(dynamicExit.TargetParams, "목표가", errors);

        // 현재 실시간 주문 엔진은 '완료된 일봉 신호 → 다음 거래일 시장가 진입'만
        // 백테스트와 동일하게 보장한다. 지원하지 않는 설정을 묵시하고 주문하지 않도록 저장 단계에서 차단한다.
        if (pattern.EnableLiveTrading)
        {
            if (pattern.TimeFrame != TimeFrame.Daily)
                errors.Add("실시간 주문은 현재 일봉 전략만 안전하게 지원합니다.");
            if (!pattern.EntryMode.Equals("NextOpen", StringComparison.OrdinalIgnoreCase))
                errors.Add("실시간 주문은 완료된 일봉 신호를 사용하도록 '다음 봉 시가'만 지원합니다.");
            if (pattern.PartialProfitR > 0 || scaling.Count > 0)
                errors.Add("부분 익절·추가 매수·분할 매도가 있는 전략은 실시간 주문을 아직 켤 수 없습니다.");
        }

        return errors.Distinct().ToArray();
    }

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
            else if (!Indicators.Contains(rule.Indicator)) errors.Add($"{prefix}: 지원하지 않는 지표입니다.");
            if (!string.IsNullOrWhiteSpace(rule.CompareIndicator) && !Indicators.Contains(rule.CompareIndicator))
                errors.Add($"{prefix}: 비교 지표가 올바르지 않습니다.");
            if (!Operators.Contains(rule.Operator)) errors.Add($"{prefix}: 비교 방식이 올바르지 않습니다.");
            if (rule.WithinBars < 0 || rule.ConsecutiveBars < 0) errors.Add($"{prefix}: 봉 수는 0 이상이어야 합니다.");
            if (rule.WithinBars > 0 && rule.ConsecutiveBars > 0) errors.Add($"{prefix}: 최근 N봉과 연속 봉은 동시에 사용할 수 없습니다.");
            if (rule.Weight <= 0) errors.Add($"{prefix}: 가중치는 0보다 커야 합니다.");
            if (rule.Params.Any(pair => pair.Value < 0)) errors.Add($"{prefix}: 지표 계산 기간은 음수일 수 없습니다.");
            ValidatePositiveParams(rule.Params, prefix, errors);
            ValidatePositiveParams(rule.CompareParams, $"{prefix} 비교 지표", errors);
            if (rule.Indicator.Equals("MACD_HIST", StringComparison.OrdinalIgnoreCase)
                && rule.Params.TryGetValue("fast", out var fast)
                && rule.Params.TryGetValue("slow", out var slow)
                && fast >= slow)
                errors.Add($"{prefix}: MACD 빠른 기간은 느린 기간보다 작아야 합니다.");
        }
    }

    private static void ValidatePositiveParams(Dictionary<string, decimal>? parameters, string scope, List<string> errors)
    {
        if (parameters is null) return;
        foreach (var (key, value) in parameters)
        {
            if (PositiveParameterKeys.Contains(key) && value <= 0)
                errors.Add($"{scope}: {key} 값은 0보다 커야 합니다.");
            if (key is "multiplier" or "multiple" or "percent" && value <= 0)
                errors.Add($"{scope}: {key} 값은 0보다 커야 합니다.");
        }
    }

    private static void ValidateLogic(string? logic, string scope, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(logic) || !Logics.Contains(logic))
            errors.Add($"{scope}: 조건 결합은 AND 또는 OR여야 합니다.");
    }

    private static T? Parse<T>(string? json, string fallbackJson, string scope, List<string> errors, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(json) ? fallbackJson : json, options);
        }
        catch (JsonException)
        {
            errors.Add($"{scope} 설정 형식이 올바르지 않습니다.");
            return default;
        }
    }
}

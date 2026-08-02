using System.Text.Json;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// 사용자 정의 규칙 기반 패턴 감지기.
/// 22종 지표 + 6종 연산자 + withinBars 메타조건을 지원합니다.
/// </summary>
public class RuleBasedDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly CustomPatternDefinition _definition;
    private readonly List<EntryRule> _rules;
    private readonly List<ConditionGroup> _entryGroups;
    private readonly string _entryGroupsLogic;
    private readonly List<WeightTier> _weightTiers;
    private readonly List<EntryRule> _exitRules;
    private readonly List<ScalingRule> _scalingRules;
    private readonly TimeFilter _timeFilter;
    private readonly DynamicExitConfig? _dynamicExit;

    // 참조 종목 데이터 (BacktestService에서 주입)
    private Dictionary<string, OhlcvBar[]>? _referenceData;

    public PatternType PatternType => PatternType.Custom;
    public string CustomPatternName => _definition.Name;
    public CustomPatternDefinition Definition => _definition;

    public RuleBasedDetector(IIndicatorService indicators, CustomPatternDefinition definition)
    {
        _indicators = indicators;
        _definition = definition;
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _rules = JsonSerializer.Deserialize<List<EntryRule>>(
            definition.EntryRulesJson, jsonOpts) ?? [];
        _entryGroups = JsonSerializer.Deserialize<List<ConditionGroup>>(
            definition.EntryGroupsJson, jsonOpts) ?? [];
        _entryGroupsLogic = definition.EntryGroupsLogic;
        _weightTiers = definition.UseWeightTiers
            ? JsonSerializer.Deserialize<List<WeightTier>>(definition.WeightTiersJson, jsonOpts) ?? []
            : [];
        _exitRules = JsonSerializer.Deserialize<List<EntryRule>>(
            definition.ExitRulesJson, jsonOpts) ?? [];
        _scalingRules = JsonSerializer.Deserialize<List<ScalingRule>>(
            definition.ScalingRulesJson, jsonOpts) ?? [];
        _timeFilter = JsonSerializer.Deserialize<TimeFilter>(
            definition.TimeFilterJson, jsonOpts) ?? new();
        var dynExit = JsonSerializer.Deserialize<DynamicExitConfig>(
            definition.DynamicExitJson, jsonOpts);
        _dynamicExit = dynExit?.StopType == "ATR" && dynExit.StopParams.Count == 0
            && dynExit.TargetType == "ATR" && dynExit.TargetParams.Count == 0
            ? null : dynExit;
    }

    /// <summary>참조 종목 데이터를 설정합니다. BacktestService에서 매 심볼 루프 전에 호출.</summary>
    public void SetReferenceData(Dictionary<string, OhlcvBar[]> refData) => _referenceData = refData;

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < 50) return Task.FromResult<PatternSignal?>(null);
        if (_definition.RequireBullRegime && !regime.SpyAbove200Ma)
            return Task.FromResult<PatternSignal?>(null);

        // 시간/계절 필터 체크
        var lastBar = bars[^1];
        if (_timeFilter.AllowedDaysOfWeek.Count > 0 &&
            !_timeFilter.AllowedDaysOfWeek.Contains((int)lastBar.Timestamp.DayOfWeek))
            return Task.FromResult<PatternSignal?>(null);
        if (_timeFilter.BlockedMonths.Count > 0 &&
            _timeFilter.BlockedMonths.Contains(lastBar.Timestamp.Month))
            return Task.FromResult<PatternSignal?>(null);

        // 공유 계산 캐시
        var ctx = new EvalContext(bars, _indicators);

        // ── 진입 조건 평가: 그룹 우선, 없으면 flat rules ──
        bool entryPassed;
        decimal matchedWeight;
        decimal totalWeight;
        string details;

        if (_entryGroups.Count > 0)
        {
            var (passed, mw, tw, desc) = EvaluateGroups(_entryGroups, _entryGroupsLogic, ctx);
            entryPassed = passed;
            matchedWeight = mw;
            totalWeight = tw;
            details = desc;
        }
        else
        {
            if (_rules.Count == 0) return Task.FromResult<PatternSignal?>(null);
            var isAnd = string.Equals(_definition.EntryLogic, "AND", StringComparison.OrdinalIgnoreCase);
            var results = new List<(bool passed, string desc, decimal weight)>();

            foreach (var rule in _rules)
            {
                var (passed, desc2) = EvaluateRule(rule, ctx);
                results.Add((passed, desc2, rule.Weight));
                if (isAnd && !passed) return Task.FromResult<PatternSignal?>(null);
                if (!isAnd && passed) break;
            }

            entryPassed = isAnd ? results.All(r => r.passed) : results.Any(r => r.passed);
            matchedWeight = results.Where(r => r.passed).Sum(r => r.weight);
            // OR 모드: 평가된 규칙만 분모, AND 모드: 전체 규칙
            totalWeight = isAnd ? _rules.Sum(r => r.Weight) : results.Sum(r => r.weight);
            details = string.Join(", ", results.Where(r => r.passed).Select(r => r.desc));
        }

        if (!entryPassed) return Task.FromResult<PatternSignal?>(null);

        var curr = bars[^1];
        var atr = ctx.GetAtr(14);
        var currentAtr = atr[^1];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        // ── 동적 손절/목표 계산 ──
        decimal stopLoss, target;
        if (_dynamicExit != null)
        {
            stopLoss = ComputeDynamicStop(curr, bars, ctx, currentAtr);
            target = ComputeDynamicTarget(curr, bars, ctx, currentAtr, curr.Close - stopLoss);
        }
        else
        {
            stopLoss = curr.Close - currentAtr * _definition.AtrStopMultiplier;
            target = curr.Close + currentAtr * _definition.AtrTargetMultiplier;
        }

        var confidence = Math.Min(1.0m, totalWeight > 0 ? matchedWeight / totalWeight : 1.0m);

        // ── 비중 단계 평가 ──
        var allocationScale = _definition.DefaultAllocationPercent / 100m;
        var weightLabel = "";
        if (_weightTiers.Count > 0)
        {
            foreach (var tier in _weightTiers)
            {
                if (tier.Conditions.Count == 0) continue;
                var tierIsAnd = string.Equals(tier.Logic, "AND", StringComparison.OrdinalIgnoreCase);
                var tierResults = new List<bool>();
                foreach (var cond in tier.Conditions)
                {
                    var (passed, _) = EvaluateRule(cond, ctx);
                    tierResults.Add(passed);
                    if (tierIsAnd && !passed) break;
                    if (!tierIsAnd && passed) break;
                }
                var tierPassed = tierIsAnd
                    ? tierResults.All(r => r)
                    : tierResults.Any(r => r);
                if (tierPassed)
                {
                    allocationScale = tier.AllocationPercent / 100m;
                    weightLabel = tier.Label;
                    break; // 첫 매칭 적용
                }
            }
        }
        if (allocationScale <= 0) return Task.FromResult<PatternSignal?>(null);

        var weightInfo = !string.IsNullOrEmpty(weightLabel) ? $" [비중:{weightLabel} {allocationScale:P0}]" : "";

        return Task.FromResult<PatternSignal?>(new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.Custom,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = confidence,
            AllocationScale = allocationScale,
            Details = $"[{_definition.Name}] {details}{weightInfo}",
            IsActive = true
        });
    }

    /// <summary>
    /// 규칙 기반 청산 조건을 평가합니다. 조건 충족 시 true 반환.
    /// BacktestService에서 매 봉마다 호출합니다.
    /// </summary>
    public bool ShouldExit(OhlcvBar[] bars)
    {
        if (_exitRules.Count == 0) return false;
        var ctx = new EvalContext(bars, _indicators);
        var isOr = string.Equals(_definition.ExitRulesLogic, "OR", StringComparison.OrdinalIgnoreCase);

        foreach (var rule in _exitRules)
        {
            var (passed, _) = EvaluateRule(rule, ctx);
            if (isOr && passed) return true;
            if (!isOr && !passed) return false;
        }
        return !isOr; // AND: all passed
    }

    /// <summary>
    /// 스케일링 조건을 평가합니다. 매칭된 스케일링 규칙 반환 (없으면 null).
    /// </summary>
    public ScalingRule? CheckScaling(OhlcvBar[] bars, decimal currentProfitPct, Dictionary<int, int> scaleCounts)
    {
        for (int i = 0; i < _scalingRules.Count; i++)
        {
            var sr = _scalingRules[i];
            // 최대 횟수 체크
            scaleCounts.TryGetValue(i, out var count);
            if (count >= sr.MaxCount) continue;
            // 최소 수익률 체크
            if (currentProfitPct < sr.MinProfitPercent) continue;
            if (sr.Conditions.Count == 0) continue;

            var ctx = new EvalContext(bars, _indicators);
            var isAnd = string.Equals(sr.Logic, "AND", StringComparison.OrdinalIgnoreCase);
            var allMatch = true;
            var anyMatch = false;
            foreach (var cond in sr.Conditions)
            {
                var (passed, _) = EvaluateRule(cond, ctx);
                if (passed) anyMatch = true;
                else allMatch = false;
                if (isAnd && !passed) break;
                if (!isAnd && passed) break;
            }
            if ((isAnd && allMatch) || (!isAnd && anyMatch))
            {
                scaleCounts[i] = count + 1;
                return sr;
            }
        }
        return null;
    }

    public bool HasExitRules => _exitRules.Count > 0;
    public bool HasScalingRules => _scalingRules.Count > 0;

    /// <summary>그룹 기반 조건 평가. 각 그룹 내 규칙을 AND/OR로 평가하고, 그룹 간 결합을 적용.</summary>
    private (bool passed, decimal matchedWeight, decimal totalWeight, string details) EvaluateGroups(
        List<ConditionGroup> groups, string groupsLogic, EvalContext ctx)
    {
        var isGroupsAnd = string.Equals(groupsLogic, "AND", StringComparison.OrdinalIgnoreCase);
        var groupResults = new List<(bool passed, string label)>();
        decimal totalWeightSum = 0, matchedWeightSum = 0;
        var allDescs = new List<string>();

        foreach (var group in groups)
        {
            if (group.Rules.Count == 0) continue;
            var isInnerAnd = string.Equals(group.Logic, "AND", StringComparison.OrdinalIgnoreCase);
            var ruleResults = new List<(bool passed, string desc, decimal weight)>();

            foreach (var rule in group.Rules)
            {
                var (p, d) = EvaluateRule(rule, ctx);
                ruleResults.Add((p, d, rule.Weight));
                if (isInnerAnd && !p) break;
                if (!isInnerAnd && p) break;
            }

            var groupPassed = isInnerAnd
                ? ruleResults.All(r => r.passed)
                : ruleResults.Any(r => r.passed);

            groupResults.Add((groupPassed, group.Label));
            totalWeightSum += group.Rules.Sum(r => r.Weight);
            matchedWeightSum += ruleResults.Where(r => r.passed).Sum(r => r.weight);

            if (groupPassed)
            {
                var label = string.IsNullOrEmpty(group.Label) ? "" : $"[{group.Label}] ";
                allDescs.Add(label + string.Join(", ", ruleResults.Where(r => r.passed).Select(r => r.desc)));
            }

            if (isGroupsAnd && !groupPassed) return (false, matchedWeightSum, totalWeightSum, "");
            if (!isGroupsAnd && groupPassed) break;
        }

        var passed = isGroupsAnd
            ? groupResults.All(r => r.passed)
            : groupResults.Any(r => r.passed);

        return (passed, matchedWeightSum, totalWeightSum, string.Join(" | ", allDescs));
    }

    /// <summary>동적 손절가 계산</summary>
    private decimal ComputeDynamicStop(OhlcvBar curr, OhlcvBar[] bars, EvalContext ctx, decimal currentAtr)
    {
        if (_dynamicExit == null) return curr.Close - currentAtr * _definition.AtrStopMultiplier;
        var p = _dynamicExit.StopParams;
        decimal GetP(string key, decimal def) => p.TryGetValue(key, out var v) ? v : def;

        return _dynamicExit.StopType.ToUpperInvariant() switch
        {
            "BOLLINGER_LOWER" =>
                ctx.GetBollinger((int)GetP("period", 20), GetP("stddev", 2.0m)).Lower[^1],
            "SMA" =>
                ctx.GetSma((int)GetP("period", 20))[^1],
            "EMA" =>
                ctx.GetEma((int)GetP("period", 20))[^1],
            "PREV_LOW" =>
                GetPrevLow(bars, (int)GetP("period", 5)),
            "PERCENT" =>
                curr.Close * (1 - GetP("percent", 2) / 100m),
            _ => // ATR
                curr.Close - currentAtr * GetP("multiplier", _definition.AtrStopMultiplier),
        };
    }

    /// <summary>동적 목표가 계산</summary>
    private decimal ComputeDynamicTarget(OhlcvBar curr, OhlcvBar[] bars, EvalContext ctx,
        decimal currentAtr, decimal riskDistance)
    {
        if (_dynamicExit == null) return curr.Close + currentAtr * _definition.AtrTargetMultiplier;
        var p = _dynamicExit.TargetParams;
        decimal GetP(string key, decimal def) => p.TryGetValue(key, out var v) ? v : def;

        return _dynamicExit.TargetType.ToUpperInvariant() switch
        {
            "BOLLINGER_UPPER" =>
                ctx.GetBollinger((int)GetP("period", 20), GetP("stddev", 2.0m)).Upper[^1],
            "SMA" =>
                ctx.GetSma((int)GetP("period", 20))[^1],
            "EMA" =>
                ctx.GetEma((int)GetP("period", 20))[^1],
            "PREV_HIGH" =>
                GetPrevHigh(bars, (int)GetP("period", 5)),
            "R_MULTIPLE" =>
                curr.Close + riskDistance * GetP("multiple", 3.0m),
            "PERCENT" =>
                curr.Close * (1 + GetP("percent", 5) / 100m),
            _ => // ATR
                curr.Close + currentAtr * GetP("multiplier", _definition.AtrTargetMultiplier),
        };
    }

    private static decimal GetPrevLow(OhlcvBar[] bars, int lookback)
    {
        decimal low = decimal.MaxValue;
        for (int i = Math.Max(0, bars.Length - 1 - lookback); i < bars.Length - 1; i++)
            if (bars[i].Low < low) low = bars[i].Low;
        return low == decimal.MaxValue ? bars[^1].Close * 0.98m : low;
    }

    private static decimal GetPrevHigh(OhlcvBar[] bars, int lookback)
    {
        decimal high = 0;
        for (int i = Math.Max(0, bars.Length - 1 - lookback); i < bars.Length - 1; i++)
            if (bars[i].High > high) high = bars[i].High;
        return high == 0 ? bars[^1].Close * 1.05m : high;
    }

    private (bool passed, string desc) EvaluateRule(EntryRule rule, EvalContext ctx)
    {
        try
        {
            // 참조 종목이 있으면 해당 종목의 EvalContext 사용
            var evalCtx = ctx;
            var refPrefix = "";
            if (!string.IsNullOrWhiteSpace(rule.RefSymbol) && _referenceData != null
                && _referenceData.TryGetValue(rule.RefSymbol.ToUpperInvariant(), out var refBars)
                && refBars.Length >= 50)
            {
                evalCtx = new EvalContext(refBars, _indicators);
                refPrefix = $"{rule.RefSymbol}:";
            }

            // 비교 대상: 다른 지표 vs 고정값
            decimal GetThresholds(int offset, out decimal prevThreshold)
            {
                if (!string.IsNullOrEmpty(rule.CompareIndicator))
                {
                    var (cmpCurr, cmpPrev) = ComputeIndicator(rule.CompareIndicator, rule.CompareParams ?? new(), evalCtx, offset);
                    prevThreshold = cmpPrev;
                    return cmpCurr;
                }
                prevThreshold = rule.Value;
                return rule.Value;
            }

            var thresholdLabel = !string.IsNullOrEmpty(rule.CompareIndicator) ? rule.CompareIndicator : $"{rule.Value}";

            // consecutiveBars: N봉 연속 조건 충족 필요
            if (rule.ConsecutiveBars > 1)
            {
                for (int offset = 0; offset < rule.ConsecutiveBars; offset++)
                {
                    if (!HasSufficientRuleHistory(rule, evalCtx, offset))
                        return (false, $"{refPrefix}{rule.Indicator} insufficient history for {rule.ConsecutiveBars} bars");

                    var (val, prevVal) = ComputeIndicator(rule.Indicator, rule.Params, evalCtx, offset);
                    var thr = GetThresholds(offset, out var prevThr);
                    if (!Compare(val, prevVal, rule.Operator, thr, prevThr))
                        return (false, $"{refPrefix}{rule.Indicator} not held {rule.ConsecutiveBars} bars");
                }
                var (lastVal, _) = ComputeIndicator(rule.Indicator, rule.Params, evalCtx, 0);
                return (true, $"{refPrefix}{rule.Indicator}={lastVal:F2} held {rule.ConsecutiveBars} bars");
            }

            // withinBars: 최근 N봉 중 하나라도 조건 충족하면 통과
            if (rule.WithinBars > 0)
            {
                var checkedBars = 0;
                for (int offset = 0; offset < rule.WithinBars; offset++)
                {
                    if (!HasSufficientRuleHistory(rule, evalCtx, offset))
                        break;

                    checkedBars++;
                    var (val, prevVal) = ComputeIndicator(rule.Indicator, rule.Params, evalCtx, offset);
                    var thr = GetThresholds(offset, out var prevThr);
                    if (Compare(val, prevVal, rule.Operator, thr, prevThr))
                        return (true, $"{refPrefix}{rule.Indicator}={val:F2} {rule.Operator} {thresholdLabel} (within {rule.WithinBars})");
                }
                if (checkedBars == 0)
                    return (false, $"{refPrefix}{rule.Indicator} insufficient history for within {rule.WithinBars}");
                return (false, $"{refPrefix}{rule.Indicator} not met within {rule.WithinBars} bars");
            }

            if (!HasSufficientRuleHistory(rule, evalCtx, 0))
                return (false, $"{refPrefix}{rule.Indicator} insufficient history");

            var (currentVal, prev) = ComputeIndicator(rule.Indicator, rule.Params, evalCtx, 0);
            var threshold = GetThresholds(0, out var prevThreshold2);
            var passed = Compare(currentVal, prev, rule.Operator, threshold, prevThreshold2);
            return (passed, $"{refPrefix}{rule.Indicator}={currentVal:F2} {rule.Operator} {thresholdLabel}");
        }
        catch
        {
            return (false, $"{rule.Indicator}: 평가 실패");
        }
    }

    private static bool Compare(decimal current, decimal prev, string op, decimal threshold, decimal prevThreshold) => op switch
    {
        ">" => current > threshold,
        "<" => current < threshold,
        ">=" => current >= threshold,
        "<=" => current <= threshold,
        "crosses_above" => prev <= prevThreshold && current > threshold,
        "crosses_below" => prev >= prevThreshold && current < threshold,
        _ => false
    };

    private static bool HasSufficientRuleHistory(EntryRule rule, EvalContext ctx, int offset)
    {
        var requiredBars = GetRequiredBars(rule.Indicator, rule.Params);
        if (!string.IsNullOrWhiteSpace(rule.CompareIndicator))
            requiredBars = Math.Max(requiredBars, GetRequiredBars(rule.CompareIndicator, rule.CompareParams));

        return ctx.Bars.Length - offset >= requiredBars;
    }

    private static int GetRequiredBars(string? indicator, Dictionary<string, decimal>? prms)
    {
        if (string.IsNullOrWhiteSpace(indicator))
            return 3;

        prms ??= new Dictionary<string, decimal>();

        int GetInt(string key, int def) =>
            prms.TryGetValue(key, out var v) ? (int)v : def;

        return indicator.ToUpperInvariant() switch
        {
            "RSI" or "PRICE_VS_SMA" or "PRICE_VS_EMA" or "BOLLINGER_POS" or "VOLUME_RATIO"
                or "ATR" or "ATR_PERCENT" or "VOLATILITY_20D" or "PRICE_VS_VWAP"
                or "CCI" or "ROC" or "WILLIAMS_R" or "CMF"
                => GetInt("period", 14) + 2,

            "CUMULATIVE_RSI" => GetInt("period", 2) + GetInt("cumulativePeriod", 2) + 2,

            "MACD_HIST" => GetInt("slow", 26) + GetInt("signal", 9) + 2,
            "PRICE_CHANGE" => GetInt("bars", 1) + 2,
            "SMA_SLOPE" => GetInt("period", 20) + GetInt("lookback", 5) + 2,
            "DIST_FROM_HIGH" or "DIST_FROM_LOW" or "BREAKOUT_HIGH" or "BREAKOUT_LOW"
                => GetInt("period", 20) + 2,
            "ADX" => GetInt("period", 14) * 2 + 1,
            "STOCHASTIC_K" => GetInt("period", 14) + 2,
            "STOCHASTIC_D" => GetInt("period", 14) + GetInt("smooth", 3) + 2,
            "OBV_SLOPE" => GetInt("lookback", 5) + 2,
            _ => 3
        };
    }

    /// <summary>
    /// offset=0 → 현재 봉, offset=1 → 1봉 전, ...
    /// 반환: (해당 봉의 값, 그 이전 봉의 값) — crosses 연산자용
    /// </summary>
    private (decimal current, decimal prev) ComputeIndicator(
        string indicator, Dictionary<string, decimal> prms, EvalContext ctx, int offset)
    {
        int GetInt(string key, int def) =>
            prms.TryGetValue(key, out var v) ? (int)v : def;
        decimal GetDec(string key, decimal def) =>
            prms.TryGetValue(key, out var v) ? v : def;

        var bars = ctx.Bars;
        var closes = ctx.Closes;
        // 인덱스: 현재 봉은 ^(1+offset), 이전 봉은 ^(2+offset)
        int ci = bars.Length - 1 - offset;
        int pi = bars.Length - 2 - offset;
        if (ci < 2 || pi < 1) return (0, 0);

        switch (indicator.ToUpperInvariant())
        {
            // ═══════════════════════════════════════════════════════════
            // 기본 지표
            // ═══════════════════════════════════════════════════════════

            case "RSI":
            {
                var period = GetInt("period", 14);
                var rsi = ctx.GetRsi(period);
                return (rsi[ci], rsi[pi]);
            }

            case "CUMULATIVE_RSI":
            {
                var period = GetInt("period", 2);
                var cumulativePeriod = GetInt("cumulativePeriod", 2);
                var cumulativeRsi = _indicators.CumulativeRsi(closes, period, cumulativePeriod);
                return (cumulativeRsi[ci], cumulativeRsi[pi]);
            }

            case "PRICE_VS_SMA":
            {
                var period = GetInt("period", 200);
                var sma = ctx.GetSma(period);
                if (sma[ci] == 0) return (0, 0);
                return ((closes[ci] - sma[ci]) / sma[ci] * 100,
                        sma[pi] == 0 ? 0 : (closes[pi] - sma[pi]) / sma[pi] * 100);
            }

            case "PRICE_VS_EMA":
            {
                var period = GetInt("period", 20);
                var ema = ctx.GetEma(period);
                if (ema[ci] == 0) return (0, 0);
                return ((closes[ci] - ema[ci]) / ema[ci] * 100,
                        ema[pi] == 0 ? 0 : (closes[pi] - ema[pi]) / ema[pi] * 100);
            }

            case "MACD_HIST":
            {
                var fast = GetInt("fast", 12);
                var slow = GetInt("slow", 26);
                var sig = GetInt("signal", 9);
                var (_, _, hist) = ctx.GetMacd(fast, slow, sig);
                return (hist[ci], hist[pi]);
            }

            case "BOLLINGER_POS":
            {
                var period = GetInt("period", 20);
                var stddev = GetDec("stddev", 2.0m);
                var (upper, _, lower) = ctx.GetBollinger(period, stddev);
                var range = upper[ci] - lower[ci];
                if (range == 0) return (0.5m, 0.5m);
                var curr = (closes[ci] - lower[ci]) / range;
                var prevRange = upper[pi] - lower[pi];
                var prev = prevRange == 0 ? 0.5m : (closes[pi] - lower[pi]) / prevRange;
                return (curr, prev);
            }

            case "VOLUME_RATIO":
            {
                var period = GetInt("period", 20);
                if (ci < period) return (0, 0);
                decimal avgVol = 0;
                for (int i = ci - period; i < ci; i++) avgVol += bars[i].Volume;
                avgVol /= period;
                if (avgVol == 0) return (0, 0);
                var curr = bars[ci].Volume / avgVol;
                decimal prevAvg = 0;
                if (pi >= period)
                {
                    for (int i = pi - period; i < pi; i++) prevAvg += bars[i].Volume;
                    prevAvg /= period;
                }
                var prev = prevAvg == 0 ? 0 : bars[pi].Volume / prevAvg;
                return (curr, prev);
            }

            case "PRICE_CHANGE":
            {
                var barsBack = GetInt("bars", 1);
                if (ci < barsBack || pi < barsBack) return (0, 0);
                var refC = closes[ci - barsBack];
                var refP = closes[pi - barsBack];
                return (refC == 0 ? 0 : (closes[ci] - refC) / refC * 100,
                        refP == 0 ? 0 : (closes[pi] - refP) / refP * 100);
            }

            case "ATR":
            {
                var period = GetInt("period", 14);
                var atr = ctx.GetAtr(period);
                return (atr[ci], atr[pi]);
            }

            case "SMA_SLOPE":
            {
                var period = GetInt("period", 20);
                var lookback = GetInt("lookback", 5);
                var sma = ctx.GetSma(period);
                if (ci < lookback || sma[ci - lookback] == 0) return (0, 0);
                var curr = (sma[ci] - sma[ci - lookback]) / sma[ci - lookback] * 100;
                var prev = (pi < lookback || sma[pi - lookback] == 0) ? 0
                    : (sma[pi] - sma[pi - lookback]) / sma[pi - lookback] * 100;
                return (curr, prev);
            }

            case "CANDLE_BODY":
            {
                static decimal Body(OhlcvBar b)
                {
                    var range = b.High - b.Low;
                    return range == 0 ? 0 : Math.Abs(b.Close - b.Open) / range;
                }
                return (Body(bars[ci]), Body(bars[pi]));
            }

            // ═══════════════════════════════════════════════════════════
            // 가격 구조
            // ═══════════════════════════════════════════════════════════

            case "DIST_FROM_HIGH":
            {
                // N일 고점 대비 현재가 거리 (%). 음수 = 고점 대비 하락
                var lookback = GetInt("period", 52);
                var start = Math.Max(0, ci - lookback);
                decimal high = 0;
                for (int i = start; i <= ci; i++) if (bars[i].High > high) high = bars[i].High;
                if (high == 0) return (0, 0);
                var curr = (closes[ci] - high) / high * 100;
                decimal prevHigh = 0;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i <= pi; i++) if (bars[i].High > prevHigh) prevHigh = bars[i].High;
                var prev = prevHigh == 0 ? 0 : (closes[pi] - prevHigh) / prevHigh * 100;
                return (curr, prev);
            }

            case "DIST_FROM_LOW":
            {
                // N일 저점 대비 현재가 거리 (%). 양수 = 저점 대비 상승
                var lookback = GetInt("period", 52);
                var start = Math.Max(0, ci - lookback);
                decimal low = decimal.MaxValue;
                for (int i = start; i <= ci; i++) if (bars[i].Low < low) low = bars[i].Low;
                if (low == decimal.MaxValue || low == 0) return (0, 0);
                var curr = (closes[ci] - low) / low * 100;
                decimal prevLow = decimal.MaxValue;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i <= pi; i++) if (bars[i].Low < prevLow) prevLow = bars[i].Low;
                var prev = (prevLow == decimal.MaxValue || prevLow == 0) ? 0
                    : (closes[pi] - prevLow) / prevLow * 100;
                return (curr, prev);
            }

            case "BREAKOUT_HIGH":
            {
                // N일 신고가 돌파: 1 = 돌파, 0 = 아님
                var lookback = GetInt("period", 20);
                var start = Math.Max(0, ci - lookback);
                decimal prevHigh = 0;
                for (int i = start; i < ci; i++) if (bars[i].High > prevHigh) prevHigh = bars[i].High;
                var curr = closes[ci] > prevHigh ? 1m : 0m;
                decimal ppHigh = 0;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i < pi; i++) if (bars[i].High > ppHigh) ppHigh = bars[i].High;
                var prev = closes[pi] > ppHigh ? 1m : 0m;
                return (curr, prev);
            }

            case "BREAKOUT_LOW":
            {
                // N일 신저가 돌파: 1 = 돌파, 0 = 아님
                var lookback = GetInt("period", 20);
                var start = Math.Max(0, ci - lookback);
                decimal prevLow = decimal.MaxValue;
                for (int i = start; i < ci; i++) if (bars[i].Low < prevLow) prevLow = bars[i].Low;
                var curr = closes[ci] < prevLow ? 1m : 0m;
                decimal ppLow = decimal.MaxValue;
                var pStart = Math.Max(0, pi - lookback);
                for (int i = pStart; i < pi; i++) if (bars[i].Low < ppLow) ppLow = bars[i].Low;
                var prev = closes[pi] < ppLow ? 1m : 0m;
                return (curr, prev);
            }

            case "GAP":
            {
                // 갭 비율 (%): (Open - PrevClose) / PrevClose * 100
                if (ci < 1) return (0, 0);
                var curr = closes[ci - 1] == 0 ? 0
                    : (bars[ci].Open - closes[ci - 1]) / closes[ci - 1] * 100;
                var prev = (pi < 1 || closes[pi - 1] == 0) ? 0
                    : (bars[pi].Open - closes[pi - 1]) / closes[pi - 1] * 100;
                return (curr, prev);
            }

            case "HIGHER_LOW":
            {
                // 연속 Higher Low 봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 20; i--)
                {
                    if (bars[i].Low > bars[i - 1].Low) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 20; i--)
                {
                    if (bars[i].Low > bars[i - 1].Low) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "LOWER_HIGH":
            {
                // 연속 Lower High 봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 20; i--)
                {
                    if (bars[i].High < bars[i - 1].High) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 20; i--)
                {
                    if (bars[i].High < bars[i - 1].High) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "INSIDE_BAR":
            {
                // 인사이드바: 1 = 현재 봉이 이전 봉 범위 내, 0 = 아님
                if (ci < 1) return (0, 0);
                var curr = (bars[ci].High <= bars[ci - 1].High && bars[ci].Low >= bars[ci - 1].Low) ? 1m : 0m;
                var prev = (pi >= 1 && bars[pi].High <= bars[pi - 1].High && bars[pi].Low >= bars[pi - 1].Low) ? 1m : 0m;
                return (curr, prev);
            }

            case "ENGULFING":
            {
                // 장악형: +1=강세 장악, -1=약세 장악, 0=아님
                if (ci < 1) return (0, 0);
                static decimal CheckEngulfing(OhlcvBar curr, OhlcvBar prev)
                {
                    var currBull = curr.Close > curr.Open;
                    var prevBull = prev.Close > prev.Open;
                    if (currBull && !prevBull && curr.Close > prev.Open && curr.Open < prev.Close) return 1m;
                    if (!currBull && prevBull && curr.Close < prev.Open && curr.Open > prev.Close) return -1m;
                    return 0m;
                }
                return (CheckEngulfing(bars[ci], bars[ci - 1]),
                        pi >= 1 ? CheckEngulfing(bars[pi], bars[pi - 1]) : 0m);
            }

            // ═══════════════════════════════════════════════════════════
            // 모멘텀 / 추세
            // ═══════════════════════════════════════════════════════════

            case "CONSECUTIVE_UP":
            {
                // 연속 양봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 30; i--)
                {
                    if (closes[i] > closes[i - 1]) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 30; i--)
                {
                    if (closes[i] > closes[i - 1]) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "CONSECUTIVE_DOWN":
            {
                // 연속 음봉 수
                int count = 0;
                for (int i = ci; i > 0 && i > ci - 30; i--)
                {
                    if (closes[i] < closes[i - 1]) count++;
                    else break;
                }
                int prevCount = 0;
                for (int i = pi; i > 0 && i > pi - 30; i--)
                {
                    if (closes[i] < closes[i - 1]) prevCount++;
                    else break;
                }
                return (count, prevCount);
            }

            case "ADX":
            {
                // ADX (Average Directional Index) 직접 계산
                var period = GetInt("period", 14);
                var adx = ComputeAdx(bars, period);
                return (ci < adx.Length ? adx[ci] : 0, pi < adx.Length ? adx[pi] : 0);
            }

            case "STOCHASTIC_K":
            {
                var period = GetInt("period", 14);
                var (k, _) = ComputeStochastic(bars, period, 3);
                return (ci < k.Length ? k[ci] : 0, pi < k.Length ? k[pi] : 0);
            }

            case "STOCHASTIC_D":
            {
                var period = GetInt("period", 14);
                var smooth = GetInt("smooth", 3);
                var (_, d) = ComputeStochastic(bars, period, smooth);
                return (ci < d.Length ? d[ci] : 0, pi < d.Length ? d[pi] : 0);
            }

            // ═══════════════════════════════════════════════════════════
            // 복합 지표
            // ═══════════════════════════════════════════════════════════

            case "ATR_PERCENT":
            {
                // ATR / 종가 * 100 (변동성 비율 %)
                var period = GetInt("period", 14);
                var atr = ctx.GetAtr(period);
                var curr = closes[ci] == 0 ? 0 : atr[ci] / closes[ci] * 100;
                var prev = closes[pi] == 0 ? 0 : atr[pi] / closes[pi] * 100;
                return (curr, prev);
            }

            case "VOLATILITY_20D":
            {
                // 20일 역사적 변동성 (연율화 표준편차)
                var period = GetInt("period", 20);
                if (ci < period) return (0, 0);
                return (CalcVol(closes, ci, period), CalcVol(closes, pi, period));
            }

            // ═══════════════════════════════════════════════════════════
            // 거래량 / VWAP 지표
            // ═══════════════════════════════════════════════════════════

            case "OBV":
            {
                var obv = ctx.GetObv();
                return (obv[ci], obv[pi]);
            }

            case "PRICE_VS_VWAP":
            {
                // 인라인 Rolling VWAP 계산 (IIndicatorService.VWAP는 누적 전용이므로 period 기반 직접 계산)
                var period = GetInt("period", 20);
                static decimal CalcRollingVwap(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period - 1) return 0;
                    decimal tpvSum = 0;
                    long volSum = 0;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        var tp = (bars[i].High + bars[i].Low + bars[i].Close) / 3m;
                        tpvSum += tp * bars[i].Volume;
                        volSum += bars[i].Volume;
                    }
                    return volSum == 0 ? 0 : tpvSum / volSum;
                }
                var vwapC = CalcRollingVwap(bars, ci, period);
                var vwapP = CalcRollingVwap(bars, pi, period);
                if (vwapC == 0) return (0, 0);
                return ((closes[ci] - vwapC) / vwapC * 100,
                        vwapP == 0 ? 0 : (closes[pi] - vwapP) / vwapP * 100);
            }

            case "OBV_SLOPE":
            {
                var lookback = GetInt("lookback", 5);
                var obv = ctx.GetObv();
                if (ci < lookback || obv[ci - lookback] == 0) return (0, 0);
                var curr = (obv[ci] - obv[ci - lookback]) / Math.Abs(obv[ci - lookback] == 0 ? 1 : obv[ci - lookback]) * 100;
                var prev = (pi < lookback || obv[pi - lookback] == 0) ? 0
                    : (obv[pi] - obv[pi - lookback]) / Math.Abs(obv[pi - lookback] == 0 ? 1 : obv[pi - lookback]) * 100;
                return (curr, prev);
            }

            // ═══════════════════════════════════════════════════════════
            // 추가 기술적 지표 (CCI, Williams %R, ROC, CMF)
            // ═══════════════════════════════════════════════════════════

            case "CCI":
            {
                var period = GetInt("period", 20);
                static decimal CalcCci(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period) return 0;
                    var typicals = new decimal[period];
                    for (int i = 0; i < period; i++)
                    {
                        var b = bars[idx - period + 1 + i];
                        typicals[i] = (b.High + b.Low + b.Close) / 3m;
                    }
                    var mean = typicals.Average();
                    var mad = typicals.Average(t => Math.Abs(t - mean));
                    return mad == 0 ? 0 : (typicals[^1] - mean) / (0.015m * mad);
                }
                return (CalcCci(bars, ci, period), CalcCci(bars, pi, period));
            }

            case "ROC":
            {
                var period = GetInt("period", 14);
                var curr = ci >= period && closes[ci - period] != 0
                    ? (closes[ci] - closes[ci - period]) / closes[ci - period] * 100 : 0;
                var prev = pi >= period && closes[pi - period] != 0
                    ? (closes[pi] - closes[pi - period]) / closes[pi - period] * 100 : 0;
                return (curr, prev);
            }

            case "WILLIAMS_R":
            {
                var period = GetInt("period", 14);
                static decimal CalcWr(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period - 1) return -50;
                    decimal hh = 0, ll = decimal.MaxValue;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        if (bars[i].High > hh) hh = bars[i].High;
                        if (bars[i].Low < ll) ll = bars[i].Low;
                    }
                    var range = hh - ll;
                    return range == 0 ? -50 : (hh - bars[idx].Close) / range * -100;
                }
                return (CalcWr(bars, ci, period), CalcWr(bars, pi, period));
            }

            case "CMF":
            {
                var period = GetInt("period", 20);
                static decimal CalcCmf(OhlcvBar[] bars, int idx, int period)
                {
                    if (idx < period) return 0;
                    decimal mfvSum = 0, volSum = 0;
                    for (int i = idx - period + 1; i <= idx; i++)
                    {
                        var range = bars[i].High - bars[i].Low;
                        var mfm = range == 0 ? 0 : ((bars[i].Close - bars[i].Low) - (bars[i].High - bars[i].Close)) / range;
                        mfvSum += mfm * bars[i].Volume;
                        volSum += bars[i].Volume;
                    }
                    return volSum == 0 ? 0 : mfvSum / volSum;
                }
                return (CalcCmf(bars, ci, period), CalcCmf(bars, pi, period));
            }

            default:
                return (0, 0);
        }
    }

    // ── 헬퍼 메서드 ──

    private static decimal CalcVol(decimal[] closes, int endIdx, int period)
    {
        if (endIdx < period) return 0;
        var returns = new decimal[period];
        for (int i = 0; i < period; i++)
        {
            var prev = closes[endIdx - period + i];
            var curr = closes[endIdx - period + i + 1];
            returns[i] = prev == 0 ? 0 : (curr - prev) / prev;
        }
        var mean = returns.Average();
        var variance = returns.Average(r => (r - mean) * (r - mean));
        return (decimal)Math.Sqrt((double)variance) * (decimal)Math.Sqrt(252);
    }

    private static decimal[] ComputeAdx(OhlcvBar[] bars, int period)
    {
        var n = bars.Length;
        var adx = new decimal[n];
        if (n < period + 1) return adx;

        var plusDm = new decimal[n];
        var minusDm = new decimal[n];
        var tr = new decimal[n];

        for (int i = 1; i < n; i++)
        {
            var upMove = bars[i].High - bars[i - 1].High;
            var downMove = bars[i - 1].Low - bars[i].Low;
            plusDm[i] = upMove > downMove && upMove > 0 ? upMove : 0;
            minusDm[i] = downMove > upMove && downMove > 0 ? downMove : 0;
            tr[i] = Math.Max(bars[i].High - bars[i].Low,
                Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close),
                         Math.Abs(bars[i].Low - bars[i - 1].Close)));
        }

        // Smoothed averages
        decimal smoothPlusDm = 0, smoothMinusDm = 0, smoothTr = 0;
        for (int i = 1; i <= period; i++)
        {
            smoothPlusDm += plusDm[i];
            smoothMinusDm += minusDm[i];
            smoothTr += tr[i];
        }

        var dx = new decimal[n];
        for (int i = period; i < n; i++)
        {
            if (i > period)
            {
                smoothPlusDm = smoothPlusDm - smoothPlusDm / period + plusDm[i];
                smoothMinusDm = smoothMinusDm - smoothMinusDm / period + minusDm[i];
                smoothTr = smoothTr - smoothTr / period + tr[i];
            }
            if (smoothTr == 0) continue;
            var plusDi = smoothPlusDm / smoothTr * 100;
            var minusDi = smoothMinusDm / smoothTr * 100;
            var diSum = plusDi + minusDi;
            dx[i] = diSum == 0 ? 0 : Math.Abs(plusDi - minusDi) / diSum * 100;
        }

        // ADX = SMA of DX
        decimal adxSum = 0;
        for (int i = period; i < period * 2 && i < n; i++) adxSum += dx[i];
        if (period * 2 <= n)
            adx[period * 2 - 1] = adxSum / period;
        for (int i = period * 2; i < n; i++)
            adx[i] = (adx[i - 1] * (period - 1) + dx[i]) / period;

        return adx;
    }

    private static (decimal[] k, decimal[] d) ComputeStochastic(OhlcvBar[] bars, int period, int smooth)
    {
        var n = bars.Length;
        var k = new decimal[n];
        var d = new decimal[n];

        for (int i = period - 1; i < n; i++)
        {
            decimal highest = 0, lowest = decimal.MaxValue;
            for (int j = i - period + 1; j <= i; j++)
            {
                if (bars[j].High > highest) highest = bars[j].High;
                if (bars[j].Low < lowest) lowest = bars[j].Low;
            }
            var range = highest - lowest;
            k[i] = range == 0 ? 50 : (bars[i].Close - lowest) / range * 100;
        }

        // %D = SMA of %K
        for (int i = period - 1 + smooth - 1; i < n; i++)
        {
            decimal sum = 0;
            for (int j = i - smooth + 1; j <= i; j++) sum += k[j];
            d[i] = sum / smooth;
        }

        return (k, d);
    }

    /// <summary>지표 계산 결과 캐시</summary>
    private sealed class EvalContext
    {
        public readonly OhlcvBar[] Bars;
        public readonly decimal[] Closes;
        private readonly IIndicatorService _ind;
        private readonly Dictionary<string, object> _cache = new();

        public EvalContext(OhlcvBar[] bars, IIndicatorService ind)
        {
            Bars = bars;
            _ind = ind;
            Closes = IndicatorService.ExtractCloses(bars);
        }

        public decimal[] GetRsi(int period) =>
            GetOrAdd($"rsi_{period}", () => _ind.RSI(Closes, period));
        public decimal[] GetSma(int period) =>
            GetOrAdd($"sma_{period}", () => _ind.SMA(Closes, period));
        public decimal[] GetEma(int period) =>
            GetOrAdd($"ema_{period}", () => _ind.EMA(Closes, period));
        public decimal[] GetAtr(int period) =>
            GetOrAdd($"atr_{period}", () => _ind.ATR(Bars, period));
        public (decimal[] Upper, decimal[] Middle, decimal[] Lower) GetBollinger(int period, decimal stddev) =>
            GetOrAdd($"bb_{period}_{stddev}", () => _ind.BollingerBands(Closes, period, stddev));
        public (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) GetMacd(int fast, int slow, int sig) =>
            GetOrAdd($"macd_{fast}_{slow}_{sig}", () => _ind.MACD(Closes, fast, slow, sig));
        public decimal[] GetObv() =>
            GetOrAdd("obv", () => _ind.OBV(Bars));

        private T GetOrAdd<T>(string key, Func<T> factory)
        {
            if (_cache.TryGetValue(key, out var cached)) return (T)cached;
            var val = factory();
            _cache[key] = val!;
            return val;
        }
    }
}

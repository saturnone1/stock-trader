using StockTrader.Models;

namespace StockTrader.Application.Optimization;

/// <summary>최적화 탐색 공간과 이웃 후보를 부작용 없이 생성한다.</summary>
public static class StrategyOptimizationSpace
{
    /// <summary>
    /// OptimizeParams로부터 모든 파라미터 조합(카르테시안 곱)을 생성합니다.
    /// 각 축을 Action&lt;OptimizeParamSnapshot&gt; 목록으로 동적 구성하여
    /// 파라미터 수가 늘어도 중첩 foreach 없이 확장 가능한 구조입니다.
    /// </summary>
    public static List<OptimizeParamSnapshot> GenerateOptimizeCombinations(OptimizeParams p)
    {
        // 각 축: 가능한 setter 액션의 목록
        // 축이 설정되지 않으면 [null setter] 1개 = 해당 파라미터 오버라이드 없음
        var axes = new List<List<Action<OptimizeParamSnapshot>>>();

        // ── 숫자형 단순 축 헬퍼 ──
        void AddNumericAxis(ParamRange? range, Action<OptimizeParamSnapshot, decimal?> setter)
        {
            var vals = range?.Enumerate().ToList();
            if (vals is { Count: > 0 })
                axes.Add(vals.Select(v => (Action<OptimizeParamSnapshot>)(s => setter(s, v))).ToList());
            else
                axes.Add(new List<Action<OptimizeParamSnapshot>> { _ => { } });
        }

        // ── 기존 5개 숫자형 축 ──
        AddNumericAxis(p.AtrStopMultiplier, (s, v) => s.AtrStopMultiplier = v);
        AddNumericAxis(p.AtrTargetMultiplier, (s, v) => s.AtrTargetMultiplier = v);
        AddNumericAxis(p.MaxHoldingBars, (s, v) => s.MaxHoldingBars = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.TrailingAtr, (s, v) => s.TrailingAtr = v);
        AddNumericAxis(p.PartialProfitR, (s, v) => s.PartialProfitR = v);

        // ── 추가 숫자형 축 ──
        AddNumericAxis(p.DefaultAllocationPercent, (s, v) => s.DefaultAllocationPercent = v);
        AddNumericAxis(p.CircuitBreakerConsecutiveLossLimit, (s, v) => s.CircuitBreakerConsecutiveLossLimit = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.CircuitBreakerCooldownBars, (s, v) => s.CircuitBreakerCooldownBars = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.CircuitBreakerMaxDrawdownPercent, (s, v) => s.CircuitBreakerMaxDrawdownPercent = v);
        AddNumericAxis(p.ReentryCooldownAfterLoss, (s, v) => s.ReentryCooldownAfterLoss = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.ReentryCooldownAfterWin, (s, v) => s.ReentryCooldownAfterWin = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.PortfolioMaxPositions, (s, v) => s.PortfolioMaxPositions = v.HasValue ? (int)v.Value : (int?)null);
        AddNumericAxis(p.PortfolioMaxSinglePercent, (s, v) => s.PortfolioMaxSinglePercent = v);
        AddNumericAxis(p.PortfolioMaxEntriesPerDay, (s, v) => s.PortfolioMaxEntriesPerDay = v.HasValue ? (int)v.Value : (int?)null);

        // ── 카테고리형 축 ──
        void AddCategoryAxis<T>(List<T>? options, Action<OptimizeParamSnapshot, T> setter)
        {
            if (options is { Count: > 0 })
                axes.Add(options.Select(v => (Action<OptimizeParamSnapshot>)(s => setter(s, v))).ToList());
            else
                axes.Add(new List<Action<OptimizeParamSnapshot>> { _ => { } });
        }

        AddCategoryAxis(p.EntryLogicOptions, (s, v) => s.EntryLogic = v);
        AddCategoryAxis(p.RequireBullRegimeOptions, (s, v) => s.RequireBullRegime = v);
        AddCategoryAxis(p.EntryModeOptions, (s, v) => s.EntryMode = v);
        AddCategoryAxis(p.SizingModeOptions, (s, v) => s.SizingMode = v);
        AddCategoryAxis(p.ExitLogicOptions, (s, v) => s.ExitLogic = v);
        AddCategoryAxis(p.TimeFrameOptions, (s, v) => s.TimeFrame = v);

        // ── 룰 파라미터 오버라이드 축 (RuleParamOverrides) ──
        // 각 RuleParamRange를 독립 축으로 처리
        foreach (var dim in p.RuleParamOverrides ?? new List<RuleParamRange>())
        {
            if (dim.Values.Count == 0) continue;
            var dimCopy = dim;
            axes.Add(dimCopy.Values.Select(val => (Action<OptimizeParamSnapshot>)(s =>
            {
                s.RuleOverrides.Add(new RuleOverrideEntry
                {
                    Scope = dimCopy.Scope,
                    RuleIndex = dimCopy.RuleIndex,
                    ParamKey = dimCopy.ParamKey,
                    Value = val
                });
            })).ToList());
        }

        // ── 룰 필드 오버라이드 축 (RuleFieldOverrides) ──
        foreach (var dim in p.RuleFieldOverrides ?? new List<RuleFieldRange>())
        {
            var dimCopy = dim;
            var setters = new List<Action<OptimizeParamSnapshot>>();
            if (dimCopy.NumericValues is { Count: > 0 })
            {
                foreach (var val in dimCopy.NumericValues)
                {
                    var v = val;
                    setters.Add(s =>
                    {
                        s.RuleFieldOverrides ??= new List<RuleFieldOverrideEntry>();
                        s.RuleFieldOverrides.Add(new RuleFieldOverrideEntry
                        {
                            Scope = dimCopy.Scope,
                            RuleIndex = dimCopy.RuleIndex,
                            FieldName = dimCopy.FieldName,
                            NumericValue = v
                        });
                    });
                }
            }
            if (dimCopy.StringValues is { Count: > 0 })
            {
                foreach (var val in dimCopy.StringValues)
                {
                    var v = val;
                    setters.Add(s =>
                    {
                        s.RuleFieldOverrides ??= new List<RuleFieldOverrideEntry>();
                        s.RuleFieldOverrides.Add(new RuleFieldOverrideEntry
                        {
                            Scope = dimCopy.Scope,
                            RuleIndex = dimCopy.RuleIndex,
                            FieldName = dimCopy.FieldName,
                            StringValue = v
                        });
                    });
                }
            }
            if (setters.Count > 0)
                axes.Add(setters);
        }

        // ── 총 조합 수 계산 (오버플로우 방지) ──
        long totalCount = 1;
        foreach (var axis in axes)
        {
            totalCount *= axis.Count;
            if (totalCount > 1_000_000) // 100만 초과 시 조기 중단
            {
                totalCount = long.MaxValue;
                break;
            }
        }

        // ── 조합 수가 적으면 전체 카르테시안 곱 생성, 많으면 재현 가능한 균등 표본 추출 ──
        const int MaxFullGeneration = 50_000;

        if (totalCount <= MaxFullGeneration)
        {
            // 전체 생성 (기존 방식)
            var result = new List<OptimizeParamSnapshot> { new() };
            foreach (var axis in axes)
            {
                var expanded = new List<OptimizeParamSnapshot>(result.Count * axis.Count);
                foreach (var existing in result)
                {
                    foreach (var setter in axis)
                    {
                        var copy = CloneParamSnapshot(existing);
                        setter(copy);
                        expanded.Add(copy);
                    }
                }
                result = expanded;
            }
            return result;
        }
        else
        {
            // 같은 입력은 항상 같은 후보를 만들도록 혼합 진법 공간에서 균등한 순번을 선택한다.
            var axisSizes = axes.Select(a => a.Count).ToArray();
            var sampleCount = Math.Min(MaxFullGeneration, (int)Math.Min(totalCount, int.MaxValue));
            var result = new List<OptimizeParamSnapshot>(sampleCount);

            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var ordinal = (long)((decimal)sampleIndex * totalCount / sampleCount);
                var indices = new int[axisSizes.Length];
                for (var i = axisSizes.Length - 1; i >= 0; i--)
                {
                    indices[i] = (int)(ordinal % axisSizes[i]);
                    ordinal /= axisSizes[i];
                }

                var snap = new OptimizeParamSnapshot();
                for (var i = 0; i < axes.Count; i++)
                    axes[i][indices[i]](snap);

                result.Add(snap);
            }
            return result;
        }
    }

    /// <summary>
    /// OptimizeParamSnapshot을 깊은 복사합니다 (카르테시안 곱 생성 시 사용).
    /// </summary>
    private static OptimizeParamSnapshot CloneParamSnapshot(OptimizeParamSnapshot src)
    {
        return new OptimizeParamSnapshot
        {
            AtrStopMultiplier = src.AtrStopMultiplier,
            AtrTargetMultiplier = src.AtrTargetMultiplier,
            MaxHoldingBars = src.MaxHoldingBars,
            TrailingAtr = src.TrailingAtr,
            PartialProfitR = src.PartialProfitR,
            RuleOverrides = src.RuleOverrides.Select(CloneRuleOverride).ToList(),
            EntryLogic = src.EntryLogic,
            RequireBullRegime = src.RequireBullRegime,
            EntryMode = src.EntryMode,
            SizingMode = src.SizingMode,
            ExitLogic = src.ExitLogic,
            TimeFrame = src.TimeFrame,
            DefaultAllocationPercent = src.DefaultAllocationPercent,
            CircuitBreakerConsecutiveLossLimit = src.CircuitBreakerConsecutiveLossLimit,
            CircuitBreakerCooldownBars = src.CircuitBreakerCooldownBars,
            CircuitBreakerMaxDrawdownPercent = src.CircuitBreakerMaxDrawdownPercent,
            ReentryCooldownAfterLoss = src.ReentryCooldownAfterLoss,
            ReentryCooldownAfterWin = src.ReentryCooldownAfterWin,
            PortfolioMaxPositions = src.PortfolioMaxPositions,
            PortfolioMaxSinglePercent = src.PortfolioMaxSinglePercent,
            PortfolioMaxEntriesPerDay = src.PortfolioMaxEntriesPerDay,
            RuleFieldOverrides = src.RuleFieldOverrides?.Select(CloneRuleFieldOverride).ToList(),
        };
    }

    private static RuleOverrideEntry CloneRuleOverride(RuleOverrideEntry entry) => new()
    {
        Scope = entry.Scope,
        RuleIndex = entry.RuleIndex,
        ParamKey = entry.ParamKey,
        Value = entry.Value,
    };

    private static RuleFieldOverrideEntry CloneRuleFieldOverride(RuleFieldOverrideEntry entry) => new()
    {
        Scope = entry.Scope,
        RuleIndex = entry.RuleIndex,
        FieldName = entry.FieldName,
        NumericValue = entry.NumericValue,
        StringValue = entry.StringValue,
    };

    /// <summary>
    /// Stage 2: 상위 결과 주변에서 이웃 조합을 생성합니다.
    /// 각 숫자형 파라미터를 ±step 만큼 변형하여 정밀 탐색합니다.
    /// </summary>
    public static List<OptimizeParamSnapshot> GenerateNeighborCombinations(
    List<OptimizeParamSnapshot> topSnapshots,
    OptimizeParams paramDef,
    int budget,
    List<OptimizeParamSnapshot> alreadyTested)
    {
        // 이미 테스트된 조합의 해시 (중복 방지)
        var testedKeys = new HashSet<string>(alreadyTested.Select(SnapshotKey));

        var neighbors = new List<OptimizeParamSnapshot>();

        // 각 숫자형 파라미터의 step 값 수집
        var perturbations = new List<(Action<OptimizeParamSnapshot, decimal> apply, Func<OptimizeParamSnapshot, decimal?> get, decimal step)>();

        void AddPerturbation(ParamRange? range, Func<OptimizeParamSnapshot, decimal?> getter, Action<OptimizeParamSnapshot, decimal> setter)
        {
            if (range == null) return;
            var step = range.Step ?? 1m;
            if (step <= 0) step = 1m;
            perturbations.Add((setter, getter, step));
        }

        AddPerturbation(paramDef.AtrStopMultiplier, s => s.AtrStopMultiplier, (s, v) => s.AtrStopMultiplier = v);
        AddPerturbation(paramDef.AtrTargetMultiplier, s => s.AtrTargetMultiplier, (s, v) => s.AtrTargetMultiplier = v);
        AddPerturbation(paramDef.MaxHoldingBars, s => s.MaxHoldingBars, (s, v) => s.MaxHoldingBars = (int)v);
        AddPerturbation(paramDef.TrailingAtr, s => s.TrailingAtr, (s, v) => s.TrailingAtr = v);
        AddPerturbation(paramDef.PartialProfitR, s => s.PartialProfitR, (s, v) => s.PartialProfitR = v);

        foreach (var snap in topSnapshots)
        {
            // 각 파라미터를 ±step 변형
            foreach (var (apply, get, step) in perturbations)
            {
                var currentVal = get(snap);
                if (currentVal == null) continue;

                foreach (var delta in new[] { -step, step, -step * 0.5m, step * 0.5m })
                {
                    var newVal = currentVal.Value + delta;
                    if (newVal < 0) continue;

                    var neighbor = CloneParamSnapshot(snap);
                    apply(neighbor, newVal);

                    var key = SnapshotKey(neighbor);
                    if (testedKeys.Contains(key)) continue;
                    testedKeys.Add(key);
                    neighbors.Add(neighbor);
                }
            }
        }

        // 예산 초과 시에도 동일 입력에서 동일 후보가 선택되도록 안정 정렬한다.
        if (neighbors.Count > budget)
            neighbors = neighbors.OrderBy(SnapshotKey, StringComparer.Ordinal).Take(budget).ToList();

        return neighbors;
    }

    /// <summary>스냅샷의 간단한 해시키 (중복 검출용)</summary>
    private static string SnapshotKey(OptimizeParamSnapshot s) =>
    $"{s.AtrStopMultiplier}|{s.AtrTargetMultiplier}|{s.MaxHoldingBars}|{s.TrailingAtr}|{s.PartialProfitR}" +
    $"|{s.EntryLogic}|{s.RequireBullRegime}|{s.EntryMode}|{s.SizingMode}|{s.ExitLogic}|{s.TimeFrame}" +
    $"|{s.DefaultAllocationPercent}|{s.CircuitBreakerConsecutiveLossLimit}|{s.CircuitBreakerCooldownBars}" +
    $"|{s.CircuitBreakerMaxDrawdownPercent}|{s.ReentryCooldownAfterLoss}|{s.ReentryCooldownAfterWin}" +
    $"|{s.PortfolioMaxPositions}|{s.PortfolioMaxSinglePercent}|{s.PortfolioMaxEntriesPerDay}" +
    $"|{string.Join(';', s.RuleOverrides.Select(r => $"{r.Scope}:{r.RuleIndex}:{r.ParamKey}:{r.Value}"))}" +
    $"|{string.Join(';', (s.RuleFieldOverrides ?? new List<RuleFieldOverrideEntry>()).Select(r => $"{r.Scope}:{r.RuleIndex}:{r.FieldName}:{r.NumericValue}:{r.StringValue}"))}";

    public static List<T> SelectDeterministicSample<T>(IReadOnlyList<T> items, int count)
    {
        if (count <= 0 || items.Count == 0) return new List<T>();
        if (count >= items.Count) return items.ToList();

        var result = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            var index = (int)((long)i * items.Count / count);
            result.Add(items[index]);
        }
        return result;
    }

    /// <summary>
    /// RuleParamRange 목록에서 카르테시안 곱을 생성합니다.
    /// 빈 목록이면 빈 오버라이드 세트 하나를 반환합니다.
    /// </summary>
    private static List<List<RuleOverrideEntry>> BuildRuleCombinations(
    List<RuleParamRange> dims)
    {
        var result = new List<List<RuleOverrideEntry>> { new() };

        foreach (var dim in dims)
        {
            if (dim.Values.Count == 0) continue;
            var expanded = new List<List<RuleOverrideEntry>>();
            foreach (var existing in result)
                foreach (var val in dim.Values)
                {
                    var copy = new List<RuleOverrideEntry>(existing)
    {
    new() { Scope = dim.Scope, RuleIndex = dim.RuleIndex, ParamKey = dim.ParamKey, Value = val }
    };
                    expanded.Add(copy);
                }
            result = expanded;
        }

        return result;
    }
}

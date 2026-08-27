using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Domain.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Application.MarketData;
using StockTrader.Engine.Rules;
using StockTrader.Engine.Strategies;
using EvalContext = StockTrader.Engine.Rules.RuleIndicatorEvaluationContext;

namespace StockTrader.Services.Patterns;

/// <summary>
/// 사용자 정의 규칙 기반 패턴 감지기.
/// 22종 지표 + 6종 연산자 + withinBars 메타조건을 지원합니다.
/// </summary>
public class RuleBasedDetector : ICustomStrategyDetector
{
    private readonly RuleIndicatorEvaluator _indicatorEvaluator;
    private readonly RuleConditionEvaluator _conditionEvaluator;
    private readonly RuleGroupEvaluator _groupEvaluator;
    private readonly CompiledStrategy _strategy;
    private readonly StrategyDocument _definition;
    private readonly List<EntryRule> _rules;
    private readonly List<ConditionGroup> _entryGroups;
    private readonly string _entryGroupsLogic;
    private readonly List<WeightTier> _weightTiers;
    private readonly TimeFilter _timeFilter;
    private readonly DynamicExitConfig? _dynamicExit;
    private readonly CompiledPositionRuleRuntime _positionRules;

    // 참조 종목 데이터 (BacktestService에서 주입)
    private Dictionary<string, StockTrader.Engine.MarketData.PriceBar[]>? _referenceData;
    private DateTime? _referenceAsOf;

    public PatternType PatternType => PatternType.Custom;
    public string CustomPatternName => _definition.Name;
    public StrategyDocument Definition => _definition;
    public CompiledStrategy Strategy => _strategy;

    internal RuleBasedDetector(
        StrategyDocument definition)
        : this(Compile(definition))
    {
    }

    internal RuleBasedDetector(
        CompiledStrategy strategy)
    {
        _indicatorEvaluator = new RuleIndicatorEvaluator();
        _conditionEvaluator = new RuleConditionEvaluator(_indicatorEvaluator);
        _groupEvaluator = new RuleGroupEvaluator(_conditionEvaluator);
        _strategy = strategy;
        _definition = strategy.Source;
        _rules = strategy.EntryRules.ToList();
        _entryGroups = strategy.EntryGroups.ToList();
        _entryGroupsLogic = strategy.Source.EntryGroupsLogic;
        _weightTiers = strategy.WeightTiers.ToList();
        _timeFilter = strategy.TimeFilter;
        _dynamicExit = strategy.DynamicExit;
        _positionRules = new CompiledPositionRuleRuntime(strategy);
    }

    private static CompiledStrategy Compile(StrategyDocument definition)
    {
        var result = StrategyCompiler.Compile(definition);
        return result.Strategy ?? throw new ArgumentException(string.Join(" ", result.Errors), nameof(definition));
    }

    /// <summary>참조 종목 데이터를 설정합니다. BacktestService에서 매 심볼 루프 전에 호출.</summary>
    public void SetReferenceData(Dictionary<string, OhlcvBar[]> refData, DateTime? asOf = null)
    {
        _referenceData = refData.ToDictionary(
            pair => pair.Key,
            pair => EnginePriceBarMapper.Map(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        _referenceAsOf = asOf;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        if (bars.Length < StrategyEvaluationPolicy.MinimumWarmupBars)
            return Task.FromResult<PatternSignal?>(null);
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
        var ctx = _indicatorEvaluator.CreateContext(EnginePriceBarMapper.Map(bars));

        // ── 진입 조건 평가: 그룹 우선, 없으면 flat rules ──
        bool entryPassed;
        decimal matchedWeight;
        decimal totalWeight;
        string details;

        if (_entryGroups.Count > 0)
        {
            var result = _groupEvaluator.Evaluate(
                _entryGroups, _entryGroupsLogic, ctx, _referenceData, _referenceAsOf);
            entryPassed = result.IsMatch;
            matchedWeight = result.MatchedWeight;
            totalWeight = result.TotalWeight;
            details = result.Details;
        }
        else
        {
            if (_rules.Count == 0) return Task.FromResult<PatternSignal?>(null);
            var isAnd = string.Equals(_definition.EntryLogic, "AND", StringComparison.OrdinalIgnoreCase);
            var results = new List<(bool passed, string desc, decimal weight)>();

            foreach (var rule in _rules)
            {
                var result = _conditionEvaluator.Evaluate(rule, ctx, _referenceData, _referenceAsOf);
                results.Add((result.IsMatch, result.Details, rule.Weight));
                if (isAnd && !result.IsMatch) return Task.FromResult<PatternSignal?>(null);
            }

            entryPassed = isAnd ? results.All(r => r.passed) : results.Any(r => r.passed);
            matchedWeight = results.Where(r => r.passed).Sum(r => r.weight);
            totalWeight = _rules.Sum(r => r.Weight);
            details = string.Join(", ", results.Where(r => r.passed).Select(r => r.desc));
        }

        if (!entryPassed) return Task.FromResult<PatternSignal?>(null);

        var curr = bars[^1];
        var atr = ctx.GetAtr(StrategyEvaluationPolicy.EntryAtrPeriod);
        var currentAtr = atr[^1];
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        var priceLevels = DynamicExitPricePolicy.Resolve(
            _dynamicExit,
            _definition.AtrStopMultiplier,
            _definition.AtrTargetMultiplier,
            bars,
            ctx,
            currentAtr);
        var stopLoss = priceLevels.Stop;
        var target = priceLevels.Target;

        if (stopLoss <= 0 || stopLoss >= curr.Close || target <= curr.Close)
            return Task.FromResult<PatternSignal?>(null);

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
                    var result = _conditionEvaluator.Evaluate(cond, ctx, _referenceData, _referenceAsOf);
                    tierResults.Add(result.IsMatch);
                    if (tierIsAnd && !result.IsMatch) break;
                    if (!tierIsAnd && result.IsMatch) break;
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
            CustomPatternName = _definition.Name,
            DetectedAt = curr.Timestamp,
            SignalBarAt = curr.Timestamp,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = target,
            Confidence = confidence,
            AllocationScale = allocationScale,
            Details = $"[{_definition.Name}] {details}{weightInfo}",
            IsActive = true
        });
    }

    public Task<PatternSignal?> EvaluateEntryAsync(
        string symbol,
        OhlcvBar[] bars,
        MarketRegime regime,
        CancellationToken ct = default) => DetectAsync(symbol, bars, regime, ct);

    /// <summary>
    /// 규칙 기반 청산 조건을 평가합니다. 조건 충족 시 true 반환.
    /// BacktestService에서 매 봉마다 호출합니다.
    /// </summary>
    public bool ShouldExit(OhlcvBar[] bars)
    {
        return _positionRules.ShouldExit(
            EnginePriceBarMapper.Map(bars), _referenceData, _referenceAsOf);
    }

    /// <summary>
    /// 스케일링 조건을 평가합니다. 실제 체결이 확정되기 전에는 실행 횟수를 변경하지 않습니다.
    /// </summary>
    public ScalingRuleMatch? EvaluateScaling(
        OhlcvBar[] bars,
        decimal currentProfitPct,
        IReadOnlyDictionary<int, int> scaleCounts)
    {
        var match = _positionRules.EvaluateScaling(
            EnginePriceBarMapper.Map(bars), currentProfitPct, scaleCounts,
            _referenceData, _referenceAsOf);
        return match is { } value ? new ScalingRuleMatch(value.RuleIndex, value.Rule) : null;
    }

    public bool HasExitRules => _positionRules.HasExitRules;
    public bool HasScalingRules => _positionRules.HasScalingRules;

}

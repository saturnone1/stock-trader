using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Strategies;

/// <summary>
/// DB/API의 문자열 JSON 정의를 한 번 해석한 실행 입력이다.
/// 미리보기·백테스트·실시간 탐지기는 이 모델을 공유한다.
/// </summary>
public sealed record CompiledStrategy(
    int SchemaVersion,
    StrategyDocument Source,
    IReadOnlyList<EntryRule> EntryRules,
    IReadOnlyList<ConditionGroup> EntryGroups,
    IReadOnlyList<EntryRule> ExitRules,
    IReadOnlyList<ConditionGroup> ExitGroups,
    IReadOnlyList<WeightTier> WeightTiers,
    IReadOnlyList<ScalingRule> ScalingRules,
    TimeFilter TimeFilter,
    CircuitBreakerConfig CircuitBreaker,
    ReentryConfig Reentry,
    PortfolioRulesConfig PortfolioRules,
    DynamicExitConfig? DynamicExit,
    IReadOnlyCollection<string> ReferenceSymbols)
{
    public string Name => Source.Name;
    public TimeFrame TimeFrame => Source.TimeFrame;
    public string EntryMode => Source.EntryMode;
    public string SizingMode => Source.SizingMode;
}

public sealed record StrategyCompilationResult(
    CompiledStrategy? Strategy,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Strategy is not null && Errors.Count == 0;
}

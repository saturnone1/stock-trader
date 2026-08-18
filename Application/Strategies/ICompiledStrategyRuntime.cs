using StockTrader.Models;

namespace StockTrader.Application.Strategies;

public sealed record ScalingRuleMatch(int RuleIndex, ScalingRule Rule);

/// <summary>
/// 검증·컴파일된 사용자 전략을 한 실행 세션에서 평가하는 결정적 런타임 경계입니다.
/// 미리보기 엔진은 구체적인 지표 서비스나 detector 구현을 알지 않고 이 포트만 사용합니다.
/// </summary>
public interface ICompiledStrategyRuntime
{
    CompiledStrategy Strategy { get; }
    bool HasExitRules { get; }
    bool HasScalingRules { get; }

    void SetReferenceData(Dictionary<string, OhlcvBar[]> referenceData, DateTime? asOf = null);
    Task<PatternSignal?> EvaluateEntryAsync(
        string symbol,
        OhlcvBar[] bars,
        MarketRegime regime,
        CancellationToken ct = default);
    bool ShouldExit(OhlcvBar[] bars);
    ScalingRuleMatch? EvaluateScaling(
        OhlcvBar[] bars,
        decimal currentProfitPercent,
        IReadOnlyDictionary<int, int> scaleCounts);
}

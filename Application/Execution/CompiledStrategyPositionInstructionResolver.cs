using StockTrader.Application.Strategies;
using StockTrader.Models;

namespace StockTrader.Application.Execution;

public sealed record CompiledStrategyPositionInstructions(
    StrategyExitInstruction? Exit,
    LongPositionScalingInstruction? Scaling);

/// <summary>
/// 검증·컴파일된 사용자 전략의 종가 청산과 추가 매수·분할 매도 조건을
/// 공통 체결 세션 입력으로 변환합니다. 미리보기, 백테스트, 실시간 실행은
/// 각자 준비한 과거 봉과 포트폴리오 한도만 전달하고 조건 의미론을 복제하지 않습니다.
/// </summary>
public static class CompiledStrategyPositionInstructionResolver
{
    public static CompiledStrategyPositionInstructions Resolve(
        ICompiledStrategyRuntime runtime,
        OhlcvBar[] bars,
        decimal executionPrice,
        decimal entryPrice,
        IReadOnlyDictionary<int, int> scalingExecutionCounts,
        decimal maxPositionCost = decimal.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(scalingExecutionCounts);

        if (bars.Length == 0)
            return new CompiledStrategyPositionInstructions(null, null);

        var exit = runtime.HasExitRules && runtime.ShouldExit(bars)
            ? new StrategyExitInstruction(
                executionPrice,
                LongPositionExecutionReasons.StrategyRuleExit)
            : null;

        LongPositionScalingInstruction? scaling = null;
        if (runtime.HasScalingRules)
        {
            var currentProfitPercent = entryPrice > 0m
                ? (executionPrice - entryPrice) / entryPrice * 100m
                : 0m;
            var match = runtime.EvaluateScaling(
                bars,
                currentProfitPercent,
                scalingExecutionCounts);
            if (match is not null)
            {
                scaling = new LongPositionScalingInstruction(
                    match.RuleIndex,
                    match.Rule.Direction,
                    match.Rule.Percent,
                    maxPositionCost);
            }
        }

        return new CompiledStrategyPositionInstructions(exit, scaling);
    }
}

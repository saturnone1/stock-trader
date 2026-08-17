namespace StockTrader.Application.Execution;

/// <summary>
/// 손절과 부분 익절 이후 적용되는 전량 청산 우선순위를 정의합니다.
/// 목표가, 전략 매도, 시간 청산 순서를 봉 기반 실행과 실시간 실행이 공유합니다.
/// </summary>
public static class LongPositionCloseDecisionPolicy
{
    public static StrategyExitInstruction? Resolve(
        decimal targetPrice,
        decimal observedTargetPrice,
        decimal timeExitPrice,
        LongPositionExitPolicy policy,
        StrategyExitInstruction? strategyExit,
        bool timeExitReached)
    {
        if (policy.EnableTargetExit
            && targetPrice > 0
            && observedTargetPrice >= targetPrice)
        {
            return new StrategyExitInstruction(targetPrice, "목표 도달");
        }

        if (strategyExit is { Price: > 0 })
            return strategyExit;

        if (policy.EnableTimeExit
            && policy.MaxHoldingBars > 0
            && timeExitReached
            && timeExitPrice > 0)
        {
            return new StrategyExitInstruction(
                timeExitPrice,
                $"시간 청산({policy.MaxHoldingBars}봉)");
        }

        return null;
    }
}

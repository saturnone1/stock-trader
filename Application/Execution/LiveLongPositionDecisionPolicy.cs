namespace StockTrader.Application.Execution;

public sealed record LiveLongPositionDecision(
    LongPositionExecutionState State,
    bool ShouldExit,
    string Reason,
    PositionExecutionEvent? StopUpdate = null);

/// <summary>
/// 실시간 현재가에서 롱 포지션의 청산 여부를 판단하는 순수 정책입니다.
/// 실제 주문과 체결가는 브로커 어댑터가 담당하며, 부분 체결은 지원 범위 밖입니다.
/// </summary>
public static class LiveLongPositionDecisionPolicy
{
    public static LiveLongPositionDecision Evaluate(
        LongPositionExecutionState state,
        decimal currentPrice,
        decimal currentAtr,
        LongPositionExitPolicy policy,
        bool timeExitReached,
        StrategyExitInstruction? strategyExit = null,
        decimal? dynamicStopFloor = null)
    {
        if (currentPrice <= 0 || state.CurrentQuantity <= 0)
            return new LiveLongPositionDecision(state, false, string.Empty);

        // 이미 확정돼 있던 손절을 먼저 판단한다. 새 보호 손절은 이번 평가가 끝날 때 갱신한다.
        if (currentPrice <= state.StopPrice)
        {
            var reason = state.BreakevenApplied || state.TrailingActivated
                ? policy.ProtectedStopReason
                : policy.StopReason;
            return new LiveLongPositionDecision(state, true, reason);
        }

        var next = state with
        {
            HighestPrice = Math.Max(state.HighestPrice, currentPrice),
            LowestPrice = state.LowestPrice == 0 ? currentPrice : Math.Min(state.LowestPrice, currentPrice),
        };

        var closeDecision = LongPositionCloseDecisionPolicy.Resolve(
            next.TargetPrice,
            currentPrice,
            currentPrice,
            policy,
            strategyExit,
            timeExitReached);
        if (closeDecision is not null)
            return new LiveLongPositionDecision(next, true, closeDecision.Reason);

        var stopUpdate = LongPositionExecutionPolicy.AdvanceProtectiveStop(
            next,
            currentPrice,
            currentAtr,
            policy,
            dynamicStopFloor);
        return new LiveLongPositionDecision(stopUpdate.State, false, string.Empty, stopUpdate.Event);
    }
}

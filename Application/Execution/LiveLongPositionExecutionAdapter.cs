using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Application.Execution;

public sealed record LiveLongPositionExecutionIntent(
    int Quantity,
    string Reason,
    PositionExecutionKind Kind,
    int? ScalingRuleIndex = null,
    bool MarksPartialProfit = false);

public sealed record LiveLongPositionExecutionDecision(
    LongPositionExecutionState State,
    LiveLongPositionExecutionIntent? Intent = null,
    LongPositionSessionEvent? StopUpdate = null)
{
    public bool ShouldExit => Intent is not null;
    public string Reason => Intent?.Reason ?? string.Empty;
}

/// <summary>
/// 현재가 스냅샷을 공통 롱 포지션 실행 세션에 투영합니다. 세션이 같은 시점에 여러
/// 체결 이벤트를 만들더라도 실시간 브로커에는 첫 주문만 제출하고, 다음 이벤트는
/// 그 체결이 내구성 있게 반영된 뒤 재평가합니다.
/// </summary>
public static class LiveLongPositionExecutionAdapter
{
    public static LiveLongPositionExecutionDecision Evaluate(
        LongPositionExecutionState state,
        int initialQuantity,
        decimal currentPrice,
        decimal currentAtr,
        LongPositionExitPolicy policy,
        bool timeExitReached,
        StrategyExitInstruction? strategyExit = null,
        decimal? dynamicStopFloor = null)
    {
        if (currentPrice <= 0 || state.CurrentQuantity <= 0)
            return new LiveLongPositionExecutionDecision(state);

        var barIndex = timeExitReached && policy.MaxHoldingBars > 0
            ? SaturatingAdd(state.EntryBarIndex, policy.MaxHoldingBars)
            : state.EntryBarIndex;
        var result = LongPositionExecutionSessionPolicy.Evaluate(
            new LongPositionSessionState(
                state,
                Math.Max(initialQuantity, state.CurrentQuantity),
                state.EntryPrice * state.CurrentQuantity,
                RealizedPnl: 0m,
                new Dictionary<int, int>()),
            new OhlcvBar
            {
                Open = currentPrice,
                High = currentPrice,
                Low = currentPrice,
                Close = currentPrice,
            },
            barIndex,
            currentAtr,
            policy,
            strategyExit,
            dynamicStopFloor);

        var execution = result.Events.FirstOrDefault(item =>
            item.Type is LongPositionSessionEventType.PartialExit
                or LongPositionSessionEventType.Exit);
        if (execution is not null)
        {
            // 주문이 실제로 체결되기 전에는 수량, 평균단가, 손익분기·부분익절 상태를
            // 앞당겨 적용하지 않는다. 관측된 고가/저가만 안전하게 보존한다.
            var observedState = state with
            {
                HighestPrice = result.State.Execution.HighestPrice,
                LowestPrice = result.State.Execution.LowestPrice,
            };
            return new LiveLongPositionExecutionDecision(
                observedState,
                new LiveLongPositionExecutionIntent(
                    execution.Quantity,
                    execution.Reason,
                    execution.Type == LongPositionSessionEventType.PartialExit
                        ? PositionExecutionKind.PartialProfit
                        : PositionExecutionKind.FullExit,
                    MarksPartialProfit:
                        execution.Type == LongPositionSessionEventType.PartialExit));
        }

        var stopUpdate = result.Events.LastOrDefault(item =>
            item.Type == LongPositionSessionEventType.StopMoved);
        return new LiveLongPositionExecutionDecision(
            result.State.Execution,
            StopUpdate: stopUpdate);
    }

    private static int SaturatingAdd(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;
}

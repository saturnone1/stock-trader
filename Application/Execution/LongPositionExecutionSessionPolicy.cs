using StockTrader.Models;

namespace StockTrader.Application.Execution;

public sealed record LongPositionSessionState(
    LongPositionExecutionState Execution,
    int InitialQuantity,
    decimal TotalCost,
    decimal RealizedPnl,
    IReadOnlyDictionary<int, int> ScalingExecutionCounts);

public sealed record LongPositionScalingInstruction(
    int RuleIndex,
    string Direction,
    decimal Percent,
    decimal MaxPositionCost = decimal.MaxValue);

public enum LongPositionSessionEventType
{
    PartialExit,
    Exit,
    StopMoved,
    ScaleIn,
    ScaleOut,
}

public sealed record LongPositionSessionEvent(
    LongPositionSessionEventType Type,
    decimal Price,
    int Quantity,
    string Reason,
    int QuantityAfter,
    decimal EntryPriceAfter,
    int? ScalingRuleIndex = null);

public sealed record LongPositionSessionResult(
    LongPositionSessionState State,
    IReadOnlyList<LongPositionSessionEvent> Events,
    bool IsClosed);

/// <summary>
/// 한 봉에서 발생하는 장중 청산, 종가 매도 규칙, 추가 매수·분할 매도와 그에 따른
/// 수량·평균단가·실현손익·실행 횟수 변경을 하나의 순수 실행 세션으로 적용합니다.
/// 미리보기와 백테스트 어댑터는 결과를 각자의 표시·원장 모델로 투영하기만 합니다.
/// </summary>
public static class LongPositionExecutionSessionPolicy
{
    public static LongPositionSessionResult Evaluate(
        LongPositionSessionState state,
        OhlcvBar bar,
        int barIndex,
        decimal currentAtr,
        LongPositionExitPolicy exitPolicy,
        StrategyExitInstruction? strategyExit = null,
        decimal? dynamicStopFloor = null,
        LongPositionScalingInstruction? scaling = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bar);

        var barResult = LongPositionExecutionPolicy.Evaluate(
            state.Execution,
            bar,
            barIndex,
            currentAtr,
            exitPolicy,
            strategyExit,
            dynamicStopFloor);
        var events = new List<LongPositionSessionEvent>();
        var realizedPnl = state.RealizedPnl;
        var runningQuantity = Math.Max(0, state.Execution.CurrentQuantity);

        foreach (var executionEvent in barResult.Events)
        {
            var type = executionEvent.Type switch
            {
                PositionExecutionEventType.PartialExit => LongPositionSessionEventType.PartialExit,
                PositionExecutionEventType.Exit => LongPositionSessionEventType.Exit,
                _ => LongPositionSessionEventType.StopMoved,
            };
            var eventQuantity = Math.Max(0, executionEvent.Quantity);
            if (type is LongPositionSessionEventType.PartialExit or LongPositionSessionEventType.Exit)
            {
                eventQuantity = Math.Min(runningQuantity, eventQuantity);
                realizedPnl += (executionEvent.Price - state.Execution.EntryPrice) * eventQuantity;
                runningQuantity -= eventQuantity;
            }

            events.Add(new LongPositionSessionEvent(
                type,
                executionEvent.Price,
                eventQuantity,
                executionEvent.Reason,
                runningQuantity,
                barResult.State.EntryPrice));
        }

        var execution = barResult.State with { CurrentQuantity = runningQuantity };
        var totalCost = execution.EntryPrice * runningQuantity;
        var next = state with
        {
            Execution = execution,
            TotalCost = totalCost,
            RealizedPnl = realizedPnl,
        };
        if (barResult.IsClosed || scaling is null)
            return new LongPositionSessionResult(next, events, barResult.IsClosed);

        var maxScaleInQuantity = scaling.MaxPositionCost == decimal.MaxValue
            ? int.MaxValue
            : LongPositionSizingPolicy.CalculateAffordableQuantity(
                Math.Max(0m, scaling.MaxPositionCost - totalCost),
                bar.Close);
        var scalingDecision = LongPositionScalingPolicy.Apply(
            new LongPositionScalingState(
                state.InitialQuantity,
                execution.CurrentQuantity,
                execution.EntryPrice,
                totalCost),
            scaling.Direction,
            scaling.Percent,
            bar.Close,
            maxScaleInQuantity);
        if (scalingDecision is null)
            return new LongPositionSessionResult(next, events, false);

        var counts = new Dictionary<int, int>(state.ScalingExecutionCounts);
        LongPositionScalingPolicy.RegisterExecution(counts, scaling.RuleIndex);
        var scalingType = scalingDecision.Action == LongPositionScalingAction.ScaleIn
            ? LongPositionSessionEventType.ScaleIn
            : LongPositionSessionEventType.ScaleOut;
        if (scalingType == LongPositionSessionEventType.ScaleOut)
        {
            realizedPnl += (bar.Close - execution.EntryPrice) * scalingDecision.ExecutedQuantity;
        }

        execution = execution with
        {
            EntryPrice = scalingDecision.State.EntryPrice,
            CurrentQuantity = scalingDecision.State.CurrentQuantity,
        };
        next = next with
        {
            Execution = execution,
            TotalCost = scalingDecision.State.TotalCost,
            RealizedPnl = realizedPnl,
            ScalingExecutionCounts = counts,
        };
        var reason = scalingType == LongPositionSessionEventType.ScaleIn
            ? $"추가 매수({scaling.Percent}%)"
            : $"분할 매도({scaling.Percent}%)";
        events.Add(new LongPositionSessionEvent(
            scalingType,
            bar.Close,
            scalingDecision.ExecutedQuantity,
            reason,
            execution.CurrentQuantity,
            execution.EntryPrice,
            scaling.RuleIndex));
        return new LongPositionSessionResult(next, events, false);
    }
}

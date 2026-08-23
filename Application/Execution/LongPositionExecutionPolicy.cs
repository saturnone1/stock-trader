using StockTrader.Engine.MarketData;

namespace StockTrader.Application.Execution;

public static class LongPositionExecutionPolicy
{
    public static LongPositionBarResult Evaluate(
        LongPositionExecutionState state,
        PriceBar bar,
        int barIndex,
        decimal currentAtr,
        LongPositionExitPolicy policy,
        StrategyExitInstruction? strategyExit = null,
        decimal? dynamicStopFloor = null)
    {
        var events = new List<PositionExecutionEvent>();
        var effectiveQuantity = Math.Max(0, state.CurrentQuantity);

        // 이 봉이 시작되기 전에 확정되어 있던 손절을 가장 먼저 처리한다.
        if (effectiveQuantity > 0 && bar.Low <= state.StopPrice)
        {
            var fill = bar.Open > 0 && bar.Open < state.StopPrice ? bar.Open : state.StopPrice;
            var stopped = state with
            {
                LowestPrice = state.LowestPrice == 0 ? fill : Math.Min(state.LowestPrice, fill),
            };
            var reason = state.BreakevenApplied || state.TrailingActivated
                ? policy.ProtectedStopReason
                : policy.StopReason;
            events.Add(new PositionExecutionEvent(PositionExecutionEventType.Exit, fill, effectiveQuantity, reason));
            return new LongPositionBarResult(stopped, events, true);
        }

        var next = state with
        {
            HighestPrice = Math.Max(state.HighestPrice, bar.High),
            LowestPrice = state.LowestPrice == 0 ? bar.Low : Math.Min(state.LowestPrice, bar.Low),
        };

        // 손절과 부분 익절 가격을 같은 봉에서 모두 통과하면 손절이 우선한다.
        if (policy.EnablePartialProfit && !next.PartialProfitTaken && effectiveQuantity >= 2)
        {
            var partialTarget = next.EntryPrice + next.RiskDistance * policy.PartialProfitRMultiple;
            if (bar.High >= partialTarget)
            {
                var sold = effectiveQuantity / 2;
                next = next with
                {
                    CurrentQuantity = effectiveQuantity - sold,
                    StopPrice = Math.Max(next.StopPrice, next.EntryPrice),
                    PartialProfitTaken = true,
                    BreakevenApplied = true,
                };
                events.Add(new PositionExecutionEvent(
                    PositionExecutionEventType.PartialExit,
                    partialTarget,
                    sold,
                    $"부분 익절({policy.PartialProfitRMultiple}R)"));
            }
        }

        var barsSinceEntry = barIndex - next.EntryBarIndex;
        var closeDecision = LongPositionCloseDecisionPolicy.Resolve(
            next.TargetPrice,
            bar.High,
            bar.Close,
            policy,
            strategyExit,
            barsSinceEntry >= policy.MaxHoldingBars);
        if (closeDecision is not null)
            return Close(next, events, closeDecision.Price, closeDecision.Reason);

        var stopUpdate = AdvanceProtectiveStop(
            next,
            bar.Close,
            currentAtr,
            policy,
            dynamicStopFloor);
        next = stopUpdate.State;
        if (stopUpdate.Event is not null)
            events.Add(stopUpdate.Event);

        return new LongPositionBarResult(next, events, false);
    }

    internal static (LongPositionExecutionState State, PositionExecutionEvent? Event) AdvanceProtectiveStop(
        LongPositionExecutionState state,
        decimal observedPrice,
        decimal currentAtr,
        LongPositionExitPolicy policy,
        decimal? dynamicStopFloor)
    {
        var next = state;
        var oldStop = state.StopPrice;
        if (!next.BreakevenApplied && next.EntryAtr > 0 && policy.BreakevenAtrMultiplier > 0
            && observedPrice >= next.EntryPrice + next.EntryAtr * policy.BreakevenAtrMultiplier)
        {
            next = next with
            {
                StopPrice = Math.Max(next.StopPrice, next.EntryPrice),
                BreakevenApplied = true,
            };
        }

        if (policy.EnableTrailingStop)
        {
            var activationPrice = next.EntryPrice + next.RiskDistance * policy.TrailingActivationR;
            if (!next.TrailingActivated && observedPrice >= activationPrice)
                next = next with { TrailingActivated = true };

            if (next.TrailingActivated && currentAtr > 0)
            {
                var chandelier = next.HighestPrice - currentAtr * policy.TrailingStopAtrMultiplier;
                if (chandelier > next.StopPrice)
                    next = next with { StopPrice = chandelier };
            }
        }

        if (dynamicStopFloor.HasValue && dynamicStopFloor.Value > next.StopPrice)
            next = next with { StopPrice = dynamicStopFloor.Value };

        if (next.StopPrice > oldStop)
        {
            return (next, new PositionExecutionEvent(
                PositionExecutionEventType.StopMoved,
                next.StopPrice,
                0,
                next.TrailingActivated ? "추적 손절가 상향" : "손절가를 매수가로 상향"));
        }

        return (next, null);
    }

    private static LongPositionBarResult Close(
        LongPositionExecutionState state,
        List<PositionExecutionEvent> events,
        decimal price,
        string reason)
    {
        events.Add(new PositionExecutionEvent(
            PositionExecutionEventType.Exit,
            price,
            state.CurrentQuantity,
            reason));
        return new LongPositionBarResult(state, events, true);
    }
}

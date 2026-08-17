using StockTrader.Models;

namespace StockTrader.Application.Execution;

public sealed record LongPositionExecutionState(
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal HighestPrice,
    decimal LowestPrice,
    decimal RiskDistance,
    decimal EntryAtr,
    int EntryBarIndex,
    int CurrentQuantity,
    bool PartialProfitTaken = false,
    bool BreakevenApplied = false,
    bool TrailingActivated = false);

public sealed record LongPositionExitPolicy(
    int MaxHoldingBars,
    bool EnableTrailingStop,
    decimal TrailingStopAtrMultiplier,
    decimal TrailingActivationR,
    bool EnablePartialProfit,
    decimal PartialProfitRMultiple,
    bool EnableTargetExit,
    bool EnableTimeExit,
    decimal BreakevenAtrMultiplier = 1.5m,
    string StopReason = "손절",
    string ProtectedStopReason = "트레일링 손절");

public enum PositionExecutionEventType
{
    PartialExit,
    Exit,
    StopMoved,
}

public sealed record PositionExecutionEvent(
    PositionExecutionEventType Type,
    decimal Price,
    int Quantity,
    string Reason);

public sealed record StrategyExitInstruction(decimal Price, string Reason);

public sealed record LongPositionBarResult(
    LongPositionExecutionState State,
    IReadOnlyList<PositionExecutionEvent> Events,
    bool IsClosed);

/// <summary>
/// 롱 포지션의 한 봉 체결 순서를 정의하는 순수 정책입니다.
/// OHLC만으로 장중 순서를 알 수 없으므로 기존 손절 → 부분 익절 → 목표/전략/시간 청산 →
/// 다음 봉 보호 손절 갱신 순으로 보수적으로 평가합니다.
/// </summary>
public static class LongPositionExecutionPolicy
{
    public static LongPositionBarResult Evaluate(
        LongPositionExecutionState state,
        OhlcvBar bar,
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

public sealed record LongEntryFill(
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal RiskDistance);

public static class LongEntryFillPolicy
{
    public static LongEntryFill? Reprice(
        decimal signalEntry,
        decimal signalStop,
        decimal signalTarget,
        decimal actualEntry,
        decimal fallbackTargetMultiple)
    {
        if (signalEntry <= 0 || signalStop <= 0 || actualEntry <= 0 || signalStop >= signalEntry)
            return null;

        var riskDistance = signalEntry - signalStop;
        if (riskDistance <= 0 || actualEntry <= riskDistance)
            return null;

        var targetMultiple = signalTarget > signalEntry
            ? (signalTarget - signalEntry) / riskDistance
            : fallbackTargetMultiple;
        if (targetMultiple <= 0)
            targetMultiple = fallbackTargetMultiple > 0 ? fallbackTargetMultiple : 1m;

        return new LongEntryFill(
            actualEntry,
            actualEntry - riskDistance,
            actualEntry + riskDistance * targetMultiple,
            riskDistance);
    }

    /// <summary>
    /// 이미 체결된 실시간 주문을 실제 평균단가에 맞춰 재기준화합니다. 정상적인 롱 신호는
    /// <see cref="Reprice"/>와 완전히 같은 결과를 사용하고, 외부 주문 입력이 비정상이어도
    /// 체결된 포지션 자체를 유실하지 않도록 기존 절대 거리 기준으로 안전하게 폴백합니다.
    /// </summary>
    public static LongEntryFill ReanchorExecutedFill(
        decimal signalEntry,
        decimal signalStop,
        decimal signalTarget,
        decimal actualEntry)
    {
        var repriced = Reprice(
            signalEntry,
            signalStop,
            signalTarget,
            actualEntry,
            fallbackTargetMultiple: 2m);
        if (repriced is not null) return repriced;

        var riskDistance = Math.Max(0m, signalEntry - signalStop);
        var targetDistance = Math.Max(0m, signalTarget - signalEntry);
        return new LongEntryFill(
            actualEntry,
            actualEntry - riskDistance,
            actualEntry + targetDistance,
            riskDistance);
    }
}

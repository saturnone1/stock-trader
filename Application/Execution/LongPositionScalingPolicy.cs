using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Execution;

public enum LongPositionScalingAction
{
    ScaleIn,
    ScaleOut
}

public sealed record LongPositionScalingState(
    int InitialQuantity,
    int CurrentQuantity,
    decimal EntryPrice,
    decimal TotalCost);

public sealed record LongPositionScalingDecision(
    LongPositionScalingAction Action,
    int ExecutedQuantity,
    LongPositionScalingState State);

/// <summary>
/// 미리보기와 백테스트가 공유하는 종가 기준 추가 매수·분할 매도 체결 정책입니다.
/// 퍼센트 수량은 최초 진입 수량을 기준으로 보수적으로 내림하며 최소 1주를 사용합니다.
/// 실행 어댑터는 추가 매수 가능 수량만 입력하고 평균단가·잔여 수량 계산은 이 정책에 맡깁니다.
/// </summary>
public static class LongPositionScalingPolicy
{
    public static void RegisterExecution(
        Dictionary<int, int> executionCounts,
        int ruleIndex)
    {
        ArgumentNullException.ThrowIfNull(executionCounts);
        if (ruleIndex < 0) throw new ArgumentOutOfRangeException(nameof(ruleIndex));

        executionCounts.TryGetValue(ruleIndex, out var count);
        executionCounts[ruleIndex] = checked(count + 1);
    }

    public static LongPositionScalingDecision? Apply(
        LongPositionScalingState state,
        string direction,
        decimal percent,
        decimal executionPrice,
        int maxScaleInQuantity = int.MaxValue)
    {
        if (state.InitialQuantity <= 0
            || state.CurrentQuantity <= 0
            || state.EntryPrice <= 0
            || executionPrice <= 0
            || percent is <= 0 or > 100)
        {
            return null;
        }

        var requestedQuantity = RequestedQuantity(state.InitialQuantity, percent);
        if (requestedQuantity <= 0) return null;

        if (string.Equals(
                direction,
                StrategyCatalog.ScalingInDirection,
                StringComparison.OrdinalIgnoreCase))
        {
            var remainingIntegerCapacity = int.MaxValue - state.CurrentQuantity;
            var executedQuantity = Math.Min(
                requestedQuantity,
                Math.Min(Math.Max(0, maxScaleInQuantity), remainingIntegerCapacity));
            if (executedQuantity <= 0) return null;

            var totalCost = EffectiveTotalCost(state) + executionPrice * executedQuantity;
            var currentQuantity = state.CurrentQuantity + executedQuantity;
            return new LongPositionScalingDecision(
                LongPositionScalingAction.ScaleIn,
                executedQuantity,
                state with
                {
                    CurrentQuantity = currentQuantity,
                    EntryPrice = totalCost / currentQuantity,
                    TotalCost = totalCost
                });
        }

        if (!string.Equals(
                direction,
                StrategyCatalog.ScalingOutDirection,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var soldQuantity = Math.Min(requestedQuantity, state.CurrentQuantity - 1);
        if (soldQuantity <= 0) return null;

        var remainingQuantity = state.CurrentQuantity - soldQuantity;
        return new LongPositionScalingDecision(
            LongPositionScalingAction.ScaleOut,
            soldQuantity,
            state with
            {
                CurrentQuantity = remainingQuantity,
                TotalCost = state.EntryPrice * remainingQuantity
            });
    }

    public static int RequestedQuantity(int initialQuantity, decimal percent)
    {
        if (initialQuantity <= 0 || percent is <= 0 or > 100) return 0;

        var rawQuantity = initialQuantity * percent / 100m;
        if (rawQuantity >= int.MaxValue) return int.MaxValue;
        return Math.Max(1, (int)Math.Floor(rawQuantity));
    }

    private static decimal EffectiveTotalCost(LongPositionScalingState state) =>
        state.TotalCost > 0 ? state.TotalCost : state.EntryPrice * state.CurrentQuantity;
}

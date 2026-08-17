namespace StockTrader.Application.Backtesting;

/// <summary>
/// 백테스트에서 전략 상태와 포트폴리오 한도를 바탕으로 신규 진입 가능 여부를 판정합니다.
/// 일반 진입과 다음 시가 예약 진입이 같은 경계값과 차단 우선순위를 사용하게 합니다.
/// </summary>
public static class BacktestEntryEligibilityPolicy
{
    public static BacktestEntryEligibilityDecision Evaluate(
        BacktestEntryEligibilityRequest request)
    {
        var effectiveMaxPositions = request.StrategyMaxPositions > 0
            ? Math.Min(request.DefaultMaxPositions, request.StrategyMaxPositions)
            : request.DefaultMaxPositions;

        if (request.OpenPositionCount >= effectiveMaxPositions)
            return Blocked(BacktestEntryBlockReason.PositionLimit, effectiveMaxPositions);
        if (request.DrawdownCircuitBreakerTripped)
            return Blocked(BacktestEntryBlockReason.DrawdownCircuitBreaker, effectiveMaxPositions);
        if (request.ConsecutiveLossCircuitBreakerEnabled
            && request.CurrentTimelineStep < request.CircuitBreakerUntilStep)
        {
            return Blocked(BacktestEntryBlockReason.ConsecutiveLossCircuitBreaker, effectiveMaxPositions);
        }
        if (request.MaxEntriesPerDay > 0
            && request.EntriesToday >= request.MaxEntriesPerDay)
        {
            return Blocked(BacktestEntryBlockReason.DailyEntryLimit, effectiveMaxPositions);
        }
        if (request.ReentryCooldownUntilBar is { } cooldownUntil
            && request.CurrentBarIndex < cooldownUntil)
        {
            return Blocked(BacktestEntryBlockReason.ReentryCooldown, effectiveMaxPositions);
        }

        return new BacktestEntryEligibilityDecision(
            true, BacktestEntryBlockReason.None, effectiveMaxPositions);
    }

    private static BacktestEntryEligibilityDecision Blocked(
        BacktestEntryBlockReason reason,
        int effectiveMaxPositions) => new(false, reason, effectiveMaxPositions);
}

public readonly record struct BacktestEntryEligibilityRequest(
    int DefaultMaxPositions,
    int StrategyMaxPositions,
    int OpenPositionCount,
    bool DrawdownCircuitBreakerTripped,
    bool ConsecutiveLossCircuitBreakerEnabled,
    int CurrentTimelineStep,
    int CircuitBreakerUntilStep,
    int MaxEntriesPerDay,
    int EntriesToday,
    int CurrentBarIndex,
    int? ReentryCooldownUntilBar);

public readonly record struct BacktestEntryEligibilityDecision(
    bool CanEnter,
    BacktestEntryBlockReason BlockReason,
    int EffectiveMaxPositions);

public enum BacktestEntryBlockReason
{
    None,
    PositionLimit,
    DrawdownCircuitBreaker,
    ConsecutiveLossCircuitBreaker,
    DailyEntryLimit,
    ReentryCooldown
}

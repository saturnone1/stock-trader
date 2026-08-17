namespace StockTrader.Application.Execution;

/// <summary>
/// 전략의 신규 진입을 허용할지 결정하는 실행 공통 정책입니다.
/// 미리보기, 백테스트, 실시간 추천은 각 환경의 상태를 이 요청으로 변환하고
/// 동일한 차단 우선순위와 포지션 한도 계산을 사용합니다.
/// </summary>
public static class StrategyEntryEligibilityPolicy
{
    public static StrategyEntryEligibilityDecision Evaluate(
        StrategyEntryEligibilityRequest request)
    {
        var effectiveMaxPositions = request.StrategyMaxPositions > 0
            ? Math.Min(request.DefaultMaxPositions, request.StrategyMaxPositions)
            : request.DefaultMaxPositions;

        if (request.OpenPositionCount >= effectiveMaxPositions)
            return Blocked(StrategyEntryBlockReason.PositionLimit, effectiveMaxPositions);
        if (request.DrawdownBlocked)
            return Blocked(StrategyEntryBlockReason.DrawdownCircuitBreaker, effectiveMaxPositions);
        if (request.ConsecutiveLossBlocked)
            return Blocked(StrategyEntryBlockReason.ConsecutiveLossCircuitBreaker, effectiveMaxPositions);
        if (request.MaxEntriesPerSession > 0
            && request.EntriesThisSession >= request.MaxEntriesPerSession)
        {
            return Blocked(StrategyEntryBlockReason.SessionEntryLimit, effectiveMaxPositions);
        }
        if (request.ReentryBlocked)
            return Blocked(StrategyEntryBlockReason.ReentryCooldown, effectiveMaxPositions);

        return new StrategyEntryEligibilityDecision(
            true, StrategyEntryBlockReason.None, effectiveMaxPositions);
    }

    private static StrategyEntryEligibilityDecision Blocked(
        StrategyEntryBlockReason reason,
        int effectiveMaxPositions) => new(false, reason, effectiveMaxPositions);
}

public readonly record struct StrategyEntryEligibilityRequest(
    int DefaultMaxPositions,
    int StrategyMaxPositions,
    int OpenPositionCount,
    bool DrawdownBlocked,
    bool ConsecutiveLossBlocked,
    int MaxEntriesPerSession,
    int EntriesThisSession,
    bool ReentryBlocked);

public readonly record struct StrategyEntryEligibilityDecision(
    bool CanEnter,
    StrategyEntryBlockReason BlockReason,
    int EffectiveMaxPositions);

public enum StrategyEntryBlockReason
{
    None,
    PositionLimit,
    DrawdownCircuitBreaker,
    ConsecutiveLossCircuitBreaker,
    SessionEntryLimit,
    ReentryCooldown
}

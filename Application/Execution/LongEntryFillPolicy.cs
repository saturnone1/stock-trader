namespace StockTrader.Application.Execution;

public sealed record LongEntryFill(
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal RiskDistance);

public static class LongEntryFillPolicy
{
    /// <summary>
    /// 신호가 유효한 목표가를 싣지 못했을 때 사용할 R 배수.
    ///
    /// 전략이 선언한 손절·목표 ATR 배수의 비율이 곧 그 전략의 손익비이므로, 폴백도
    /// 같은 기하를 따라야 한다. 이 값을 상수로 고정하면 전략 정의를 무시한 목표가가
    /// 만들어지고, 그 상수를 한 엔진만 쓰면 preview 와 backtest 가 같은 신호에서
    /// 서로 다른 목표가를 만든다. 두 경로 모두 이 함수를 통해 값을 얻는다.
    /// </summary>
    public static decimal ResolveFallbackTargetMultiple(
        decimal atrStopMultiplier,
        decimal atrTargetMultiplier) =>
        atrStopMultiplier > 0 && atrTargetMultiplier > 0
            ? atrTargetMultiplier / atrStopMultiplier
            : 1m;

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

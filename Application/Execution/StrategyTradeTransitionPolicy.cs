using StockTrader.Models;

namespace StockTrader.Application.Execution;

public sealed record StrategyTradeTransitionState(
    int ConsecutiveLosses = 0,
    int ReentryBlockedUntilStep = 0,
    int CircuitBreakerBlockedUntilStep = 0);

public sealed record StrategyTradeTransitionRequest(
    int CurrentReentryStep,
    int CurrentCircuitBreakerStep,
    decimal RealizedPnl,
    ReentryConfig Reentry,
    CircuitBreakerConfig CircuitBreaker);

/// <summary>
/// 완료 거래가 재진입 대기와 연속손실 중단 상태에 미치는 영향을 실행 방식과 무관하게 계산합니다.
/// 각 실행기는 봉 인덱스나 타임라인 스텝을 같은 순서 단위로 전달합니다.
/// </summary>
public static class StrategyTradeTransitionPolicy
{
    public static StrategyTradeTransitionState Apply(
        StrategyTradeTransitionState state,
        StrategyTradeTransitionRequest request)
    {
        var isLoss = request.RealizedPnl < 0;
        var cooldownSteps = ResolveReentryCooldownSteps(
            request.RealizedPnl,
            request.Reentry);
        var reentryBlockedUntil = cooldownSteps > 0
            ? request.CurrentReentryStep + cooldownSteps + 1
            : state.ReentryBlockedUntilStep;

        var consecutiveLosses = isLoss ? state.ConsecutiveLosses + 1 : 0;
        var circuitBreakerBlockedUntil = state.CircuitBreakerBlockedUntilStep;
        if (request.CircuitBreaker.ConsecutiveLossLimit > 0
            && consecutiveLosses >= request.CircuitBreaker.ConsecutiveLossLimit)
        {
            circuitBreakerBlockedUntil = request.CurrentCircuitBreakerStep
                + request.CircuitBreaker.CooldownBars + 1;
            consecutiveLosses = 0;
        }

        return new StrategyTradeTransitionState(
            consecutiveLosses,
            reentryBlockedUntil,
            circuitBreakerBlockedUntil);
    }

    public static int ResolveReentryCooldownSteps(decimal realizedPnl, ReentryConfig reentry) =>
        realizedPnl < 0
            ? reentry.CooldownBarsAfterLoss
            : reentry.CooldownBarsAfterWin;

    public static int CountTrailingLosses(IEnumerable<decimal> realizedPnls) =>
        realizedPnls.Reverse().TakeWhile(pnl => pnl < 0).Count();
}

public sealed record StrategyHistoricalCooldownDecision(
    bool ReentryBlocked,
    bool ConsecutiveLossBlocked);

/// <summary>
/// 저장된 실거래 이력을 거래일 단위의 재진입·연속손실 차단 상태로 투영합니다.
/// </summary>
public static class StrategyHistoricalCooldownPolicy
{
    /// <summary>
    /// 재진입·연속손실 차단 여부를 판정한다.
    /// 쿨다운은 봉 수(거래일 수)로 정의되므로 만료일도 실제 거래일로 세어야 한다.
    /// <paramref name="isTradingDay"/> 는 호출자가 거래소 캘린더를 연결한다. 휴장일을
    /// 거래일로 세면 쿨다운이 실제보다 일찍 풀려 차단해야 할 재진입을 허용하게 된다.
    /// </summary>
    public static StrategyHistoricalCooldownDecision Evaluate(
        IReadOnlyList<StrategyCompletedTrade> trades,
        ReentryConfig reentry,
        CircuitBreakerConfig circuitBreaker,
        DateTime asOfUtc,
        Func<DateOnly, bool> isTradingDay)
    {
        if (trades.Count == 0)
            return new(false, false);

        var chronologicalTrades = trades
            .OrderBy(trade => trade.ExitedAt)
            .ThenBy(trade => trade.SequenceId)
            .ToArray();
        var latest = chronologicalTrades[^1];
        var reentrySteps = StrategyTradeTransitionPolicy.ResolveReentryCooldownSteps(
            latest.RealizedPnl,
            reentry);
        var reentryBlocked = reentrySteps > 0
            && asOfUtc.Date <= AddTradingDays(latest.ExitedAt.Date, reentrySteps, isTradingDay);

        var trailingLosses = StrategyTradeTransitionPolicy.CountTrailingLosses(
            chronologicalTrades.Select(trade => trade.RealizedPnl));
        var consecutiveLossBlocked = circuitBreaker.ConsecutiveLossLimit > 0
            && trailingLosses >= circuitBreaker.ConsecutiveLossLimit
            && asOfUtc.Date <= AddTradingDays(
                latest.ExitedAt.Date,
                circuitBreaker.CooldownBars,
                isTradingDay);
        return new(reentryBlocked, consecutiveLossBlocked);
    }

    private static DateTime AddTradingDays(
        DateTime date, int tradingDays, Func<DateOnly, bool> isTradingDay)
    {
        var result = date.Date;
        var remaining = Math.Max(0, tradingDays);

        // 쿨다운 자체가 짧으므로 탐색 상한은 넉넉하되 유한해야 한다. 판정이 계속
        // 거짓이면 그만 세고 마지막 날짜를 반환한다 — 차단을 무한히 연장하지 않는다.
        var maximumCalendarDays = Math.Max(1, remaining) * 7 + 30;
        var examined = 0;

        while (remaining > 0 && examined < maximumCalendarDays)
        {
            result = result.AddDays(1);
            examined++;
            if (!isTradingDay(DateOnly.FromDateTime(result)))
                continue;
            remaining--;
        }

        return result;
    }
}

public sealed record StrategyDrawdownState(
    decimal PeakEquity,
    bool IsBlocked = false);

/// <summary>현재 실현 자산을 관찰해 최고점과 최대낙폭 중단 상태를 결정합니다.</summary>
public static class StrategyDrawdownPolicy
{
    public static StrategyDrawdownState Observe(
        StrategyDrawdownState state,
        decimal currentEquity,
        decimal maxDrawdownPercent)
    {
        var peakEquity = Math.Max(state.PeakEquity, currentEquity);
        if (state.IsBlocked || maxDrawdownPercent <= 0 || peakEquity <= 0)
            return new StrategyDrawdownState(peakEquity, state.IsBlocked);

        var drawdownPercent = (peakEquity - currentEquity) / peakEquity * 100m;
        return new StrategyDrawdownState(
            peakEquity,
            drawdownPercent >= maxDrawdownPercent);
    }

    public static StrategyDrawdownState EvaluateHistory(
        decimal initialEquity,
        IEnumerable<decimal> realizedPnls,
        decimal maxDrawdownPercent)
    {
        var equity = Math.Max(1m, initialEquity);
        var state = new StrategyDrawdownState(equity);
        foreach (var pnl in realizedPnls)
        {
            equity += pnl;
            state = Observe(state, equity, maxDrawdownPercent);
        }
        return state;
    }
}

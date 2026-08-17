using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>완료 거래가 전략별 재진입 대기와 연속손실 차단 상태에 미치는 전이를 적용합니다.</summary>
internal static class BacktestStrategyTransitionPolicy
{
    public static void RegisterClosedTrade(
        string strategySymbolKey,
        int currentBarIndex,
        int currentTimelineStep,
        TradeRecord trade,
        BacktestStrategyRuntime runtime,
        Dictionary<string, int> cooldowns)
    {
        var cooldownBars = trade.PnL < 0
            ? runtime.Reentry.CooldownBarsAfterLoss
            : runtime.Reentry.CooldownBarsAfterWin;
        if (cooldownBars > 0)
            cooldowns[strategySymbolKey] = currentBarIndex + cooldownBars + 1;

        if (runtime.CircuitBreaker.ConsecutiveLossLimit <= 0) return;

        if (trade.PnL >= 0)
        {
            runtime.ConsecutiveLosses = 0;
            return;
        }

        runtime.ConsecutiveLosses++;
        if (runtime.ConsecutiveLosses < runtime.CircuitBreaker.ConsecutiveLossLimit) return;

        runtime.CircuitBreakerUntilStep =
            currentTimelineStep + runtime.CircuitBreaker.CooldownBars + 1;
        runtime.ConsecutiveLosses = 0;
    }
}

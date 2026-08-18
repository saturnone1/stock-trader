using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class StrategyTradeTransitionPolicyTests
{
    [Fact]
    public void ApplyUsesIndependentReentryAndCircuitBreakerTimelines()
    {
        var request = Request(reentryStep: 10, circuitBreakerStep: 20, pnl: -10m);

        var first = StrategyTradeTransitionPolicy.Apply(new(), request);

        first.Should().Be(new StrategyTradeTransitionState(
            ConsecutiveLosses: 1,
            ReentryBlockedUntilStep: 13,
            CircuitBreakerBlockedUntilStep: 0));

        var second = StrategyTradeTransitionPolicy.Apply(
            first,
            Request(reentryStep: 20, circuitBreakerStep: 30, pnl: -5m));

        second.Should().Be(new StrategyTradeTransitionState(
            ConsecutiveLosses: 0,
            ReentryBlockedUntilStep: 23,
            CircuitBreakerBlockedUntilStep: 34));
    }

    [Fact]
    public void ApplyResetsLossesAndUsesWinCooldown()
    {
        var result = StrategyTradeTransitionPolicy.Apply(
            new StrategyTradeTransitionState(ConsecutiveLosses: 1),
            Request(reentryStep: 10, circuitBreakerStep: 20, pnl: 10m));

        result.Should().Be(new StrategyTradeTransitionState(
            ConsecutiveLosses: 0,
            ReentryBlockedUntilStep: 12,
            CircuitBreakerBlockedUntilStep: 0));
    }

    [Fact]
    public void ApplyPreservesExistingCooldownWhenNewTradeConfiguresNoWait()
    {
        var request = Request(reentryStep: 20, circuitBreakerStep: 30, pnl: 10m) with
        {
            Reentry = new ReentryConfig()
        };

        var result = StrategyTradeTransitionPolicy.Apply(
            new StrategyTradeTransitionState(ReentryBlockedUntilStep: 15),
            request);

        result.ReentryBlockedUntilStep.Should().Be(15);
    }

    [Fact]
    public void ObserveDrawdownTracksPeakAndTripsAtConfiguredPercent()
    {
        var peak = StrategyDrawdownPolicy.Observe(
            new StrategyDrawdownState(100m),
            currentEquity: 120m,
            maxDrawdownPercent: 10m);
        var drawdown = StrategyDrawdownPolicy.Observe(
            peak,
            currentEquity: 108m,
            maxDrawdownPercent: 10m);

        peak.Should().Be(new StrategyDrawdownState(120m, false));
        drawdown.Should().Be(new StrategyDrawdownState(120m, true));
        StrategyDrawdownPolicy.Observe(drawdown, 130m, 10m)
            .Should().Be(new StrategyDrawdownState(130m, true));
    }

    [Fact]
    public void ObserveDrawdownStaysOpenWhenDisabled()
    {
        StrategyDrawdownPolicy.Observe(
                new StrategyDrawdownState(100m),
                currentEquity: 1m,
                maxDrawdownPercent: 0m)
            .Should().Be(new StrategyDrawdownState(100m, false));
    }

    [Fact]
    public void HistoricalCooldownUsesWeekdaysAndTheSameTrailingLossRule()
    {
        var trades = new List<TradeRecord>
        {
            new() { Id = 2, ExitTime = new DateTime(2025, 1, 3), PnL = -5m },
            new() { Id = 1, ExitTime = new DateTime(2025, 1, 2), PnL = 10m },
            new() { Id = 3, ExitTime = new DateTime(2025, 1, 3), PnL = -2m }
        };
        var reentry = new ReentryConfig { CooldownBarsAfterLoss = 2 };
        var breaker = new CircuitBreakerConfig
        {
            ConsecutiveLossLimit = 2,
            CooldownBars = 3
        };

        StrategyHistoricalCooldownPolicy.Evaluate(
                trades, reentry, breaker, new DateTime(2025, 1, 6))
            .Should().Be(new StrategyHistoricalCooldownDecision(true, true));
        StrategyHistoricalCooldownPolicy.Evaluate(
                trades, reentry, breaker, new DateTime(2025, 1, 8))
            .Should().Be(new StrategyHistoricalCooldownDecision(false, true));
        StrategyHistoricalCooldownPolicy.Evaluate(
                trades, reentry, breaker, new DateTime(2025, 1, 9))
            .Should().Be(new StrategyHistoricalCooldownDecision(false, false));
    }

    [Fact]
    public void EvaluateDrawdownHistoryUsesRealizedPnlInChronologicalOrder()
    {
        StrategyDrawdownPolicy.EvaluateHistory(
                initialEquity: 100m,
                realizedPnls: [20m, -12m],
                maxDrawdownPercent: 10m)
            .Should().Be(new StrategyDrawdownState(120m, true));
    }

    private static StrategyTradeTransitionRequest Request(
        int reentryStep,
        int circuitBreakerStep,
        decimal pnl) => new(
        reentryStep,
        circuitBreakerStep,
        pnl,
        new ReentryConfig
        {
            CooldownBarsAfterLoss = 2,
            CooldownBarsAfterWin = 1
        },
        new CircuitBreakerConfig
        {
            ConsecutiveLossLimit = 2,
            CooldownBars = 3
        });
}

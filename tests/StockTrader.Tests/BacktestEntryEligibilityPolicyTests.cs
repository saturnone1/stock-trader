using FluentAssertions;
using StockTrader.Application.Backtesting;

namespace StockTrader.Tests;

public class BacktestEntryEligibilityPolicyTests
{
    [Fact]
    public void Evaluate_UsesTheTighterStrategyPositionLimit()
    {
        var decision = Evaluate(strategyMaxPositions: 2, openPositionCount: 2);

        decision.CanEnter.Should().BeFalse();
        decision.BlockReason.Should().Be(BacktestEntryBlockReason.PositionLimit);
        decision.EffectiveMaxPositions.Should().Be(2);
    }

    [Theory]
    [InlineData(true, false, 0, 0, BacktestEntryBlockReason.DrawdownCircuitBreaker)]
    [InlineData(false, true, 10, 0, BacktestEntryBlockReason.ConsecutiveLossCircuitBreaker)]
    [InlineData(false, false, 0, 2, BacktestEntryBlockReason.DailyEntryLimit)]
    public void Evaluate_BlocksStrategyRuntimeLimits(
        bool drawdownTripped,
        bool consecutiveLossEnabled,
        int circuitBreakerUntilStep,
        int entriesToday,
        BacktestEntryBlockReason expectedReason)
    {
        var decision = Evaluate(
            drawdownTripped: drawdownTripped,
            consecutiveLossEnabled: consecutiveLossEnabled,
            circuitBreakerUntilStep: circuitBreakerUntilStep,
            maxEntriesPerDay: 2,
            entriesToday: entriesToday);

        decision.CanEnter.Should().BeFalse();
        decision.BlockReason.Should().Be(expectedReason);
    }

    [Fact]
    public void Evaluate_CooldownBlocksBeforeBoundaryAndAllowsAtBoundary()
    {
        Evaluate(currentBarIndex: 9, reentryCooldownUntilBar: 10)
            .BlockReason.Should().Be(BacktestEntryBlockReason.ReentryCooldown);

        Evaluate(currentBarIndex: 10, reentryCooldownUntilBar: 10)
            .CanEnter.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_CircuitBreakerAllowsAtItsBoundary()
    {
        Evaluate(
                consecutiveLossEnabled: true,
                currentTimelineStep: 10,
                circuitBreakerUntilStep: 10)
            .CanEnter.Should().BeTrue();
    }

    private static BacktestEntryEligibilityDecision Evaluate(
        int strategyMaxPositions = 0,
        int openPositionCount = 0,
        bool drawdownTripped = false,
        bool consecutiveLossEnabled = false,
        int currentTimelineStep = 5,
        int circuitBreakerUntilStep = 0,
        int maxEntriesPerDay = 0,
        int entriesToday = 0,
        int currentBarIndex = 5,
        int? reentryCooldownUntilBar = null) =>
        BacktestEntryEligibilityPolicy.Evaluate(new BacktestEntryEligibilityRequest(
            DefaultMaxPositions: 5,
            StrategyMaxPositions: strategyMaxPositions,
            OpenPositionCount: openPositionCount,
            DrawdownCircuitBreakerTripped: drawdownTripped,
            ConsecutiveLossCircuitBreakerEnabled: consecutiveLossEnabled,
            CurrentTimelineStep: currentTimelineStep,
            CircuitBreakerUntilStep: circuitBreakerUntilStep,
            MaxEntriesPerDay: maxEntriesPerDay,
            EntriesToday: entriesToday,
            CurrentBarIndex: currentBarIndex,
            ReentryCooldownUntilBar: reentryCooldownUntilBar));
}

using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class StrategyEntryEligibilityPolicyTests
{
    [Fact]
    public void Evaluate_UsesOneDeterministicBlockPriorityAcrossExecutionModes()
    {
        var decision = StrategyEntryEligibilityPolicy.Evaluate(
            new StrategyEntryEligibilityRequest(
                DefaultMaxPositions: 5,
                StrategyMaxPositions: 2,
                OpenPositionCount: 2,
                DrawdownBlocked: true,
                ConsecutiveLossBlocked: true,
                MaxEntriesPerSession: 1,
                EntriesThisSession: 1,
                ReentryBlocked: true));

        decision.CanEnter.Should().BeFalse();
        decision.BlockReason.Should().Be(StrategyEntryBlockReason.PositionLimit);
        decision.EffectiveMaxPositions.Should().Be(2);
    }

    [Theory]
    [InlineData(true, false, 0, 0, false, StrategyEntryBlockReason.DrawdownCircuitBreaker)]
    [InlineData(false, true, 0, 0, false, StrategyEntryBlockReason.ConsecutiveLossCircuitBreaker)]
    [InlineData(false, false, 2, 2, false, StrategyEntryBlockReason.SessionEntryLimit)]
    [InlineData(false, false, 0, 0, true, StrategyEntryBlockReason.ReentryCooldown)]
    public void Evaluate_MapsEveryRuntimeGate(
        bool drawdownBlocked,
        bool consecutiveLossBlocked,
        int maxEntries,
        int entries,
        bool reentryBlocked,
        StrategyEntryBlockReason expected)
    {
        var decision = StrategyEntryEligibilityPolicy.Evaluate(
            new StrategyEntryEligibilityRequest(
                5, 0, 0,
                drawdownBlocked,
                consecutiveLossBlocked,
                maxEntries,
                entries,
                reentryBlocked));

        decision.BlockReason.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_AllowsEntryAtUnblockedBoundaries()
    {
        var decision = StrategyEntryEligibilityPolicy.Evaluate(
            new StrategyEntryEligibilityRequest(
                DefaultMaxPositions: 5,
                StrategyMaxPositions: 2,
                OpenPositionCount: 1,
                DrawdownBlocked: false,
                ConsecutiveLossBlocked: false,
                MaxEntriesPerSession: 2,
                EntriesThisSession: 1,
                ReentryBlocked: false));

        decision.CanEnter.Should().BeTrue();
        decision.BlockReason.Should().Be(StrategyEntryBlockReason.None);
        decision.EffectiveMaxPositions.Should().Be(2);
    }
}

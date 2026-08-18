using FluentAssertions;
using Moq;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class CompiledStrategyPositionInstructionResolverTests
{
    [Fact]
    public void ResolveProjectsCanonicalExitAndScalingFromOneRuntimeEvaluation()
    {
        var bars = new[]
        {
            new OhlcvBar { Symbol = "TQQQ", Close = 112.5m }
        };
        var counts = new Dictionary<int, int> { [2] = 1 };
        var runtime = new Mock<ICompiledStrategyRuntime>();
        runtime.SetupGet(value => value.HasExitRules).Returns(true);
        runtime.SetupGet(value => value.HasScalingRules).Returns(true);
        runtime.Setup(value => value.ShouldExit(bars)).Returns(true);
        runtime.Setup(value => value.EvaluateScaling(bars, 12.5m, counts))
            .Returns(new ScalingRuleMatch(
                2,
                new ScalingRule
                {
                    Direction = StrategyCatalog.ScalingOutDirection,
                    Percent = 25m
                }));

        var result = CompiledStrategyPositionInstructionResolver.Resolve(
            runtime.Object,
            bars,
            executionPrice: 112.5m,
            entryPrice: 100m,
            counts,
            maxPositionCost: 5_000m);

        result.Exit.Should().Be(new StrategyExitInstruction(
            112.5m,
            LongPositionExecutionReasons.StrategyRuleExit));
        result.Scaling.Should().Be(new LongPositionScalingInstruction(
            2,
            StrategyCatalog.ScalingOutDirection,
            25m,
            5_000m));
        runtime.Verify(value => value.ShouldExit(bars), Times.Once);
        runtime.Verify(value => value.EvaluateScaling(bars, 12.5m, counts), Times.Once);
    }

    [Fact]
    public void ResolveWithNoBarsDoesNotInvokeRuntimeRules()
    {
        var runtime = new Mock<ICompiledStrategyRuntime>(MockBehavior.Strict);

        var result = CompiledStrategyPositionInstructionResolver.Resolve(
            runtime.Object,
            [],
            100m,
            100m,
            new Dictionary<int, int>());

        result.Should().Be(new CompiledStrategyPositionInstructions(null, null));
        runtime.VerifyNoOtherCalls();
    }
}

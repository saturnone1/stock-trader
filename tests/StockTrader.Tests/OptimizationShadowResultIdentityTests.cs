using FluentAssertions;
using StockTrader.Application.Optimization;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Tests;

public class OptimizationShadowResultIdentityTests
{
    [Fact]
    public void EquivalentDecimalScales_HaveTheSameShadowIdentity()
    {
        var first = Result(10.0m);
        var second = Result(10m);

        OptimizationShadowResultIdentity.Compute(first)
            .Should().Be(OptimizationShadowResultIdentity.Compute(second));
    }

    private static OptimizationWorkerComputeResult Result(decimal value) => new(
        2,
        "shadow-optimization-compute-v1",
        "input-hash",
        1,
        1,
        50,
        new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        [new OptimizationWorkerCandidateResult(
            1,
            "{}",
            value,
            value,
            value,
            value,
            value,
            1,
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            value,
            1,
            value,
            value,
            value)]);
}

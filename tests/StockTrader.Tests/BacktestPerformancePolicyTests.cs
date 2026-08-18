using FluentAssertions;
using StockTrader.Application.Backtesting;

namespace StockTrader.Tests;

public class BacktestPerformancePolicyTests
{
    [Fact]
    public void Evaluate_UsesTheFullRequestedPeriod_NotTheActiveTradeWindow()
    {
        var result = BacktestPerformancePolicy.Evaluate(
            totalReturnFraction: 0.10m,
            maxDrawdownFraction: 0.05m,
            completedTradeReturnFractions: [0.10m, -0.02m],
            evaluationFrom: new DateTime(2024, 1, 1),
            evaluationTo: new DateTime(2025, 1, 1));

        result.AnnualizedReturnFraction.Should().BeApproximately(0.10m, 0.0003m);
        result.CalmarRatio.Should().BeApproximately(2m, 0.005m);
    }

    [Fact]
    public void Evaluate_ReturnsCompleteLossFloor_WhenEquityIsExhausted()
    {
        var result = BacktestPerformancePolicy.Evaluate(
            totalReturnFraction: -1m,
            maxDrawdownFraction: 1m,
            completedTradeReturnFractions: [],
            evaluationFrom: new DateTime(2024, 1, 1),
            evaluationTo: new DateTime(2025, 1, 1));

        result.AnnualizedReturnFraction.Should().Be(-1m);
        result.CalmarRatio.Should().Be(-1m);
    }

    [Fact]
    public void Evaluate_ReturnsZeroForNonPositivePeriodOrMissingDrawdown()
    {
        var instant = new DateTime(2025, 1, 1);

        var result = BacktestPerformancePolicy.Evaluate(0.10m, 0m, [], instant, instant);

        result.AnnualizedReturnFraction.Should().Be(0m);
        result.CalmarRatio.Should().Be(0m);
    }

    [Fact]
    public void RiskAdjustedRatios_AnnualizeTradeFrequencyOverTheFullEvaluationPeriod()
    {
        decimal[] returns = [0.10m, -0.02m];
        var evaluationFrom = new DateTime(2024, 1, 1);
        var evaluationTo = new DateTime(2025, 1, 1);
        var expectedAnnualization = (decimal)Math.Sqrt((double)(2m / 366m * 365.25m));
        var mean = returns.Average();
        var sampleDeviation = (decimal)Math.Sqrt((double)returns.Sum(value =>
            (value - mean) * (value - mean)) / (returns.Length - 1));
        var downsideDeviation = (decimal)Math.Sqrt((double)returns
            .Select(value => value < 0 ? value * value : 0m)
            .Average());

        BacktestPerformancePolicy.ComputeSharpeRatio(returns, evaluationFrom, evaluationTo)
            .Should().BeApproximately(mean / sampleDeviation * expectedAnnualization, 0.000001m);
        BacktestPerformancePolicy.ComputeSortinoRatio(returns, evaluationFrom, evaluationTo)
            .Should().BeApproximately(mean / downsideDeviation * expectedAnnualization, 0.000001m);
    }

    [Fact]
    public void Evaluate_UsesOneDayFloorAndCapsNumericallyExplosiveCagr()
    {
        var result = BacktestPerformancePolicy.Evaluate(
            totalReturnFraction: 1m,
            maxDrawdownFraction: 0.10m,
            completedTradeReturnFractions: [1m, -0.01m],
            evaluationFrom: new DateTime(2025, 1, 1, 9, 30, 0),
            evaluationTo: new DateTime(2025, 1, 1, 9, 31, 0));

        result.AnnualizedReturnFraction.Should().Be(10_000m);
        result.CalmarRatio.Should().Be(100_000m);
    }
}

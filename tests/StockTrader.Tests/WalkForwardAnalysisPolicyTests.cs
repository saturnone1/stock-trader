using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Models;

namespace StockTrader.Tests;

public class WalkForwardAnalysisPolicyTests
{
    [Fact]
    public void BuildPlanProducesNonOverlappingIsAndOosCalendarRanges()
    {
        var plan = WalkForwardAnalysisPolicy.BuildPlan(
            new DateTime(2024, 1, 1),
            new DateTime(2026, 7, 1),
            inSampleMonths: 12,
            outOfSampleMonths: 3);

        plan.IsValid.Should().BeTrue();
        plan.Periods.Should().Equal(
            new WalkForwardPeriod(
                new DateTime(2024, 1, 1), new DateTime(2024, 12, 31),
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 31)),
            new WalkForwardPeriod(
                new DateTime(2025, 4, 1), new DateTime(2026, 3, 31),
                new DateTime(2026, 4, 1), new DateTime(2026, 6, 30)));
        plan.Periods.Zip(plan.Periods.Skip(1)).Should().OnlyContain(pair =>
            pair.First.OutOfSampleTo < pair.Second.InSampleFrom);
        plan.Periods.Should().OnlyContain(period =>
            period.InSampleTo < period.OutOfSampleFrom);
    }

    [Theory]
    [InlineData(0, 3, "학습 기간")]
    [InlineData(12, 0, "검증 기간")]
    [InlineData(-1, 3, "학습 기간")]
    public void BuildPlanRejectsNonProgressingPeriods(
        int inSampleMonths,
        int outOfSampleMonths,
        string expectedReason)
    {
        var plan = WalkForwardAnalysisPolicy.BuildPlan(
            new DateTime(2024, 1, 1),
            new DateTime(2026, 1, 1),
            inSampleMonths,
            outOfSampleMonths);

        plan.IsValid.Should().BeFalse();
        plan.Periods.Should().BeEmpty();
        plan.ValidationError.Should().Contain(expectedReason);
    }

    [Fact]
    public void BuildPlanRejectsARangeTooShortForOneCompleteWindow()
    {
        var plan = WalkForwardAnalysisPolicy.BuildPlan(
            new DateTime(2024, 1, 1),
            new DateTime(2025, 3, 31),
            inSampleMonths: 12,
            outOfSampleMonths: 3);

        plan.IsValid.Should().BeFalse();
        plan.ValidationError.Should().Contain("완전한 워크포워드");
    }

    [Fact]
    public void AggregateUsesOosWindowMetricsAndReportsAverageSharpe()
    {
        var windows = new List<WalkForwardWindow>
        {
            new()
            {
                InSampleReturnPercent = 0.10m,
                OutOfSampleReturn = 50m,
                OutOfSampleReturnPercent = 0.05m,
                OutOfSampleMaxDrawdown = 0.08m,
                OutOfSampleSharpe = 1.2m
            },
            new()
            {
                InSampleReturnPercent = -0.02m,
                OutOfSampleReturn = -10m,
                OutOfSampleReturnPercent = -0.01m,
                OutOfSampleMaxDrawdown = 0.12m,
                OutOfSampleSharpe = -0.2m
            }
        };

        var result = WalkForwardAnalysisPolicy.Aggregate(windows);

        result.AggregateOosReturn.Should().Be(40m);
        result.AggregateOosReturnPercent.Should().Be(0.02m);
        result.AggregateOosMaxDrawdown.Should().Be(0.12m);
        result.AggregateOosWinRate.Should().Be(0.5m);
        result.AggregateOosSharpe.Should().Be(0.5m);
        result.WalkForwardEfficiency.Should().Be(0.5m);
    }
}

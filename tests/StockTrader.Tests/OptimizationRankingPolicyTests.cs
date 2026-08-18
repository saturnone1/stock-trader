using FluentAssertions;
using StockTrader.Application.Optimization;
using StockTrader.Domain.Optimization;

namespace StockTrader.Tests;

public class OptimizationRankingPolicyTests
{
    [Fact]
    public void CatalogHasUniqueCodesOneDefaultAndStableLegacyNormalization()
    {
        OptimizationRankingCatalog.All.Select(item => item.Code).Should().OnlyHaveUniqueItems();
        OptimizationRankingCatalog.All.Should().ContainSingle(item => item.IsDefault)
            .Which.Code.Should().Be(OptimizationRankingCatalog.DefaultCode);
        OptimizationRankingCatalog.Normalize(" SHARPERATIO ").Should().Be(
            OptimizationRankingCatalog.SharpeRatioCode);
        OptimizationRankingCatalog.Normalize(" sharperation ").Should().Be(
            OptimizationRankingCatalog.SharpeRatioCode);
        OptimizationRankingCatalog.Normalize("unsupported").Should().Be(
            OptimizationRankingCatalog.DefaultCode);
    }

    [Theory]
    [InlineData(OptimizationRankingCatalog.TotalReturnCode)]
    [InlineData(OptimizationRankingCatalog.SortinoRatioCode)]
    [InlineData(OptimizationRankingCatalog.SharpeRatioCode)]
    [InlineData(OptimizationRankingCatalog.CalmarRatioCode)]
    [InlineData(OptimizationRankingCatalog.ProfitFactorCode)]
    [InlineData(OptimizationRankingCatalog.WinRateCode)]
    [InlineData(OptimizationRankingCatalog.AnnualizedReturnCode)]
    public void ResultRankerUsesEveryCatalogMetric(string rankBy)
    {
        var baseline = new OptimizeResultItem { Id = 1 };
        var winner = new OptimizeResultItem { Id = 2 };
        SetMetric(winner, OptimizationRankingCatalog.MetricFor(rankBy), 1m);

        var ranked = OptimizationResultRanker.RankOptimizeResults(
            [baseline, winner], rankBy, maxResults: 2);

        ranked.Select(item => item.Id).Should().Equal(2, 1);
        ranked.Select(item => item.Rank).Should().Equal(1, 2);
    }

    [Fact]
    public void SharedPolicyCanRankOosMetricSnapshotsWithoutChangingMissingValueSemantics()
    {
        var ranked = OptimizationRankingPolicy.OrderDescending(
            new[] { "missing", "measured" },
            OptimizationRankingCatalog.AnnualizedReturnCode,
            value => new OptimizationMetricValues(
                0, 0, 0, 0, 0, 0,
                value == "measured" ? 0.12m : decimal.MinValue));

        ranked.Should().Equal("measured", "missing");
    }

    private static void SetMetric(
        OptimizeResultItem result,
        OptimizationRankMetric metric,
        decimal value)
    {
        switch (metric)
        {
            case OptimizationRankMetric.TotalReturn: result.TotalReturn = value; break;
            case OptimizationRankMetric.SortinoRatio: result.SortinoRatio = value; break;
            case OptimizationRankMetric.SharpeRatio: result.SharpeRatio = value; break;
            case OptimizationRankMetric.CalmarRatio: result.CalmarRatio = value; break;
            case OptimizationRankMetric.ProfitFactor: result.ProfitFactor = value; break;
            case OptimizationRankMetric.WinRate: result.WinRate = value; break;
            case OptimizationRankMetric.AnnualizedReturn: result.AnnualizedReturn = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(metric), metric, null);
        }
    }
}

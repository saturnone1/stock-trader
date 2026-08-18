using FluentAssertions;
using Moq;
using StockTrader.Application.Research;

namespace StockTrader.Tests;

public sealed class FinancialFactorQueryServiceTests
{
    [Fact]
    public async Task MetaUsesOneDeterministicLatestSnapshotPerSymbol()
    {
        var service = CreateService();

        var result = await service.GetMetaAsync();

        result.TotalSnapshots.Should().Be(4);
        result.SymbolsCovered.Should().Be(2);
        result.LatestAsOfDate.Should().Be(new DateTime(2025, 1, 3));
        result.Coverage.Should().Be(new FinancialFactorCoverage(2, 1, 2, 2, 2, 1));
    }

    [Fact]
    public async Task QueryPreservesGrowthTurnaroundSummaryAndFilterSemantics()
    {
        var service = CreateService();

        var result = await service.QueryAsync(new FinancialFactorQuery(
            PeRatioMax: 15m,
            PositiveEarningsOnly: true,
            Symbols: " aaa, missing ",
            SortBy: "revenueGrowthDesc"));

        result.TotalUniverse.Should().Be(2);
        result.Matched.Should().Be(1);
        var row = result.Items.Should().ContainSingle().Which;
        row.Symbol.Should().Be("AAA");
        row.PeRatio.Should().Be(12m);
        row.RevenueGrowthYoY.Should().Be(0.2m);
        row.NetIncomeGrowthYoY.Should().Be(3m);
        row.IsTurnaround.Should().BeTrue();
        row.HasPositiveEarnings.Should().BeTrue();
        row.Name.Should().Be("Alpha");
        result.Comparison.Overall.Count.Should().Be(2);
        result.Comparison.Filtered.Should().Be(new FinancialFactorSummary(
            1, 12m, 2m, 20m, 0.2m, 3m, 1, 1));
    }

    private static FinancialFactorQueryService CreateService()
    {
        var store = new Mock<IResearchUniverseStore>();
        store.Setup(item => item.LoadFinancialResearchDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialResearchDataSet(
            [
                Snapshot(1, "AAA", new DateTime(2024, 12, 31), 20m, updatedDay: 1),
                Snapshot(2, "AAA", new DateTime(2025, 1, 3), 14m, updatedDay: 2),
                Snapshot(3, "AAA", new DateTime(2025, 1, 3), 12m, updatedDay: 3),
                Snapshot(
                    4,
                    "BBB",
                    new DateTime(2025, 1, 2),
                    30m,
                    updatedDay: 3,
                    pbRatio: null,
                    revenueCurrent: 90m,
                    revenuePrevious: 100m,
                    netIncomeCurrent: -1m,
                    netIncomePrevious: 2m)
            ],
            [
                new ResearchTickerSnapshot("AAA", "Alpha", "Technology", "Software", 100m),
                new ResearchTickerSnapshot("BBB", "Beta", "Finance", "Banking", 200m)
            ]));
        return new FinancialFactorQueryService(store.Object);
    }

    private static ResearchFinancialSnapshot Snapshot(
        long id,
        string symbol,
        DateTime asOf,
        decimal peRatio,
        int updatedDay,
        decimal? pbRatio = 2m,
        decimal? revenueCurrent = 120m,
        decimal? revenuePrevious = 100m,
        decimal? netIncomeCurrent = 10m,
        decimal? netIncomePrevious = -5m) => new(
        id,
        symbol,
        asOf,
        "Test",
        peRatio,
        pbRatio,
        20m,
        15m,
        revenueCurrent,
        revenuePrevious,
        -1m,
        5m,
        netIncomeCurrent,
        netIncomePrevious,
        new DateTime(2025, 2, updatedDay));
}

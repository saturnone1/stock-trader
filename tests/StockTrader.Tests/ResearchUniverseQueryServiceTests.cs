using FluentAssertions;
using Moq;
using StockTrader.Application.Research;

namespace StockTrader.Tests;

public sealed class ResearchUniverseQueryServiceTests
{
    private static readonly IReadOnlyList<ResearchTickerSnapshot> Tickers =
    [
        new("AAA", "Alpha", "Technology", "Software", 100m),
        new("BBB", "Beta", "Finance", "Banking", 200m),
        new("CCC", "Gamma", "Technology", "Hardware", 300m),
        new("ZERO", "No cap", "Technology", "Software", 0m)
    ];

    [Fact]
    public async Task MetaUsesAllActiveTickersAndStableFacetOrdering()
    {
        var service = CreateService();

        var result = await service.GetMetaAsync();

        result.TotalActive.Should().Be(4);
        result.MarketCapCoverage.Should().Be(3);
        result.Sectors.Should().Equal(
            new ResearchFacet("Technology", 3),
            new ResearchFacet("Finance", 1));
        result.Industries[0].Should().Be(new ResearchFacet("Software", 2));
    }

    [Fact]
    public async Task QueryRanksTheWholeMarketCapUniverseBeforeFiltering()
    {
        var service = CreateService();

        var result = await service.QueryAsync(new ResearchUniverseQuery(
            Sectors: " technology ",
            PercentileMin: 50m,
            SortBy: "marketCapDesc",
            Limit: 1));

        result.TotalUniverse.Should().Be(3);
        result.Matched.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Should().Be(
            new ResearchUniverseRow(
                "CCC", "Gamma", "Technology", "Hardware", 300m, 100m));
    }

    [Fact]
    public async Task SingleTickerReceivesTheExistingHundredthPercentileConvention()
    {
        var service = CreateService([new("ONLY", "Only", "", "", 10m)]);

        var result = await service.QueryAsync(new ResearchUniverseQuery());

        result.Items.Should().ContainSingle()
            .Which.MarketCapPercentile.Should().Be(100m);
    }

    private static ResearchUniverseQueryService CreateService(
        IReadOnlyList<ResearchTickerSnapshot>? tickers = null)
    {
        var store = new Mock<IResearchUniverseStore>();
        store.Setup(item => item.LoadActiveTickersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tickers ?? Tickers);
        return new ResearchUniverseQueryService(store.Object);
    }
}

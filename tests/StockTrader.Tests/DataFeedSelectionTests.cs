using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;

namespace StockTrader.Tests;

public class DataFeedSelectionTests
{
    [Fact]
    public async Task SelectAsync_ReportsPreferredLsSourceForTheKoreanRegimeBenchmark()
    {
        var lsFeed = new Mock<IDataFeedService>().Object;
        var services = new ServiceCollection()
            .AddKeyedSingleton(DataSource.LsSecurities, lsFeed)
            .BuildServiceProvider();
        var settings = new Mock<ISettingsRepository>();
        settings.Setup(repository => repository.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings
            {
                PreferredDataSource = DataSource.LsSecurities
            });
        var factory = new DataFeedServiceFactory(
            services,
            settings.Object,
            Options.Create(new AlpacaSettings()),
            NullLogger<DataFeedServiceFactory>.Instance);

        var selection = await factory.SelectAsync(null);

        selection.Source.Should().Be(DataSource.LsSecurities);
        selection.Service.Should().BeSameAs(lsFeed);
        MarketRegimeBenchmarkPolicy.Resolve(selection.Source)
            .Should().Be(MarketRegimeBenchmarkPolicy.KoreaBenchmark);
    }

    [Fact]
    public async Task SelectAsync_ReportsYahooWhenUnconfiguredAlpacaFallsBack()
    {
        var yahooFeed = new Mock<IDataFeedService>().Object;
        var services = new ServiceCollection()
            .AddKeyedSingleton(DataSource.Yahoo, yahooFeed)
            .BuildServiceProvider();
        var settings = new Mock<ISettingsRepository>(MockBehavior.Strict);
        var factory = new DataFeedServiceFactory(
            services,
            settings.Object,
            Options.Create(new AlpacaSettings()),
            NullLogger<DataFeedServiceFactory>.Instance);

        var selection = await factory.SelectAsync(DataSource.Alpaca);

        selection.Source.Should().Be(DataSource.Yahoo);
        selection.Service.Should().BeSameAs(yahooFeed);
        MarketRegimeBenchmarkPolicy.Resolve(selection.Source)
            .Should().Be(MarketRegimeBenchmarkPolicy.UnitedStatesBenchmark);
    }
}

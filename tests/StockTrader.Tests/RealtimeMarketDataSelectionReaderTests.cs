using FluentAssertions;
using Moq;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.DataFeed;

namespace StockTrader.Tests;

public sealed class RealtimeMarketDataSelectionReaderTests
{
    [Fact]
    public async Task SelectionPreservesProviderAndNormalizesWatchlistOnce()
    {
        var settings = new Mock<ISettingsRepository>();
        settings.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings
            {
                PreferredDataSource = DataSource.LsSecurities,
                WatchlistSymbols = [" 005930 ", "005930", "000660", " "]
            });

        var result = await new RealtimeMarketDataSelectionReader(settings.Object).ReadAsync();

        result.Source.Should().Be(DataSource.LsSecurities);
        result.WatchlistSymbols.Should().Equal("005930", "000660");
    }
}

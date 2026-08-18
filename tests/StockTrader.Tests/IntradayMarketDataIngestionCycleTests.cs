using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.DataFeed;

namespace StockTrader.Tests;

public sealed class IntradayMarketDataIngestionCycleTests
{
    private readonly Mock<IIntradayMarketDataIngestionData> _data = new();
    private readonly Mock<IIntradayMarketDataIngestionSession> _session = new();
    private readonly Mock<IRealtimeMarketDataStatus> _realtime = new();
    private readonly Mock<IMarketCalendar> _calendar = new();

    [Fact]
    public async Task PollingWaitsForTheSelectedProviderMarketOnly()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.UnitedStates))
            .Returns(false);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.Korea))
            .Returns(true);

        var result = await CreateSut().RunAsync();

        result.Should().Be(new IntradayMarketDataIngestionResult(
            IntradayMarketDataIngestionStatus.MarketClosed,
            DataSource.Yahoo));
        _calendar.Verify(service => service.IsMarketOpen(MarketRegion.UnitedStates), Times.Once);
        _calendar.Verify(service => service.IsMarketOpen(MarketRegion.Korea), Times.Never);
        _session.Verify(service => service.FetchLatestBarAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActiveRealtimeFeedSuppressesOnlyItsOwnProvider()
    {
        ConfigureSession(DataSource.Alpaca, ["AAPL"]);
        _realtime.SetupGet(service => service.ActiveSource).Returns(DataSource.Alpaca);

        var result = await CreateSut().RunAsync();

        result.Status.Should().Be(IntradayMarketDataIngestionStatus.RealtimeStreamActive);
        _calendar.Verify(service => service.IsMarketOpen(It.IsAny<MarketRegion>()), Times.Never);
        _session.Verify(service => service.FetchLatestBarAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProviderTransitionWaitsForTheOldRealtimeFeedToStop()
    {
        ConfigureSession(DataSource.LsSecurities, ["005930"]);
        _realtime.SetupGet(service => service.ConnectedSource).Returns(DataSource.Alpaca);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.Korea)).Returns(true);

        var result = await CreateSut().RunAsync();

        result.Status.Should().Be(IntradayMarketDataIngestionStatus.RealtimeProviderTransition);
        _session.Verify(service => service.FetchLatestBarAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulBarsAreSavedAsOneBatchBeforeSymbolsArePublished()
    {
        ConfigureSession(DataSource.LsSecurities, ["005930", "000660"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.Korea)).Returns(true);
        var first = Bar("005930", 70_000m);
        var second = Bar("000660", 120_000m);
        _session.Setup(service => service.FetchLatestBarAsync(
                "005930", It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        _session.Setup(service => service.FetchLatestBarAsync(
                "000660", It.IsAny<CancellationToken>()))
            .ReturnsAsync(second);
        var sequence = new MockSequence();
        _session.InSequence(sequence).Setup(service => service.SaveBarsAsync(
                It.Is<IReadOnlyList<OhlcvBar>>(bars =>
                    bars.SequenceEqual(new[] { first, second })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _session.InSequence(sequence).Setup(service => service.PublishIngestedSymbolsAsync(
                It.Is<IReadOnlyList<string>>(symbols =>
                    symbols.SequenceEqual(new[] { "005930", "000660" })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().RunAsync();

        result.Should().Be(new IntradayMarketDataIngestionResult(
            IntradayMarketDataIngestionStatus.Completed,
            DataSource.LsSecurities,
            TotalSymbols: 2,
            IngestedSymbols: 2));
    }

    [Fact]
    public async Task PerSymbolFailureDoesNotDiscardOtherBarsAndRemainsVisible()
    {
        ConfigureSession(DataSource.Alpaca, ["AAPL", "TQQQ"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.UnitedStates)).Returns(true);
        var tqqq = Bar("TQQQ", 50m);
        _session.Setup(service => service.FetchLatestBarAsync(
                "AAPL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary provider failure"));
        _session.Setup(service => service.FetchLatestBarAsync(
                "TQQQ", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tqqq);

        var result = await CreateSut().RunAsync();

        result.Should().Be(new IntradayMarketDataIngestionResult(
            IntradayMarketDataIngestionStatus.PartiallyFailed,
            DataSource.Alpaca,
            TotalSymbols: 2,
            IngestedSymbols: 1,
            FailedSymbols: 1));
        _session.Verify(service => service.SaveBarsAsync(
            It.Is<IReadOnlyList<OhlcvBar>>(bars => bars.Count == 1 && bars[0] == tqqq),
            It.IsAny<CancellationToken>()), Times.Once);
        _session.Verify(service => service.PublishIngestedSymbolsAsync(
            It.Is<IReadOnlyList<string>>(symbols => symbols.SequenceEqual(new[] { "TQQQ" })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TotalProviderFailureEscapesSoTheWorkerCanRetryAndCoolDown()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL", "TQQQ"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.UnitedStates)).Returns(true);
        _session.Setup(service => service.FetchLatestBarAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));

        var act = () => CreateSut().RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*failed for all 2 symbols from Yahoo*");
        _session.Verify(service => service.SaveBarsAsync(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()), Times.Never);
        _session.Verify(service => service.PublishIngestedSymbolsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NullProviderSampleIsNotPublishedAsAnIngestedSymbol()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.UnitedStates)).Returns(true);
        _session.Setup(service => service.FetchLatestBarAsync(
                "AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OhlcvBar?)null);

        var result = await CreateSut().RunAsync();

        result.Status.Should().Be(IntradayMarketDataIngestionStatus.Completed);
        result.IngestedSymbols.Should().Be(0);
        _session.Verify(service => service.SaveBarsAsync(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()), Times.Never);
        _session.Verify(service => service.PublishIngestedSymbolsAsync(
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoAPartialFailure()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        _calendar.Setup(service => service.IsMarketOpen(MarketRegion.UnitedStates)).Returns(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _session.Setup(service => service.FetchLatestBarAsync(
                "AAPL", cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var act = () => CreateSut().RunAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _session.Verify(service => service.SaveBarsAsync(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private IntradayMarketDataIngestionCycle CreateSut() => new(
        _data.Object,
        _realtime.Object,
        _calendar.Object,
        NullLogger<IntradayMarketDataIngestionCycle>.Instance);

    private void ConfigureSession(DataSource source, IReadOnlyList<string> symbols)
    {
        _session.SetupGet(value => value.Source).Returns(source);
        _session.SetupGet(value => value.WatchlistSymbols).Returns(symbols);
        _data.Setup(service => service.OpenSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session.Object);
    }

    private static OhlcvBar Bar(string symbol, decimal close) => new()
    {
        Symbol = symbol,
        TimeFrame = TimeFrame.OneMinute,
        Timestamp = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1_000
    };
}

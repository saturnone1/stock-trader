using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.DataFeed;

namespace StockTrader.Tests;

public sealed class DailyMarketDataSyncCycleTests
{
    private static readonly DateTimeOffset Observation =
        new(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);

    private readonly Mock<IDailyMarketDataSyncData> _data = new();
    private readonly Mock<IDailyMarketDataSyncSession> _session = new();
    private readonly Mock<IMarketCalendar> _calendar = new();
    private readonly DailyMarketDataSyncState _state = new();

    public DailyMarketDataSyncCycleTests()
    {
        // 거래일 판정은 실제 거래소 캘린더에 위임한다. 동기화 대상 날짜가 휴장일인지는
        // 이 테스트가 검증하려는 대상이 아니지만, 가짜 값을 넣으면 캘린더가 바뀌었을 때
        // 테스트가 현실과 어긋난 채로 통과하게 된다.
        _calendar.Setup(service => service.GetTradingDay(
                It.IsAny<MarketRegion>(), It.IsAny<DateOnly>()))
            .Returns((MarketRegion market, DateOnly date) =>
                ExchangeCalendarCatalog.GetTradingDay(market, date));
    }

    [Fact]
    public async Task ScheduledSyncWaitsForTheSelectedProviderMarket()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        _calendar.Setup(service => service.GetLocalTime(
                MarketRegion.UnitedStates, Observation.UtcDateTime))
            .Returns(new DateTime(2026, 8, 18, 12, 0, 0));
        _calendar.Setup(service => service.GetMarketClose(MarketRegion.UnitedStates))
            .Returns(new TimeSpan(16, 0, 0));

        var result = await CreateSut().RunScheduledAsync();

        result.Status.Should().Be(DailyMarketDataSyncStatus.NotReady);
        _calendar.Verify(service => service.GetLocalTime(
            MarketRegion.Korea, It.IsAny<DateTime>()), Times.Never);
        _session.Verify(service => service.GetLastStoredBarAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _session.Verify(service => service.RefreshStatisticsAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompletedSyncUsesTheKoreanMarketDateAndDeduplicatesIt()
    {
        ConfigureSession(DataSource.LsSecurities, ["005930"]);
        ConfigureReady(MarketRegion.Korea, new DateTime(2026, 8, 18, 16, 30, 0));
        _session.Setup(service => service.GetLastStoredBarAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);
        _session.Setup(service => service.FetchBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string symbol, DateTime _, DateTime _, CancellationToken _) =>
                Bars(symbol, 2));

        var sut = CreateSut();
        var first = await sut.RunScheduledAsync();
        var second = await sut.RunScheduledAsync();

        first.Should().Be(new DailyMarketDataSyncResult(
            DailyMarketDataSyncStatus.Completed,
            TotalSymbols: 2,
            SyncedSymbols: 2,
            SyncedBars: 4));
        second.Status.Should().Be(DailyMarketDataSyncStatus.AlreadyCompleted);
        _session.Verify(service => service.FetchBarsAsync(
            It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _session.Verify(service => service.RefreshStatisticsAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProviderChangeInvalidatesCompletionOnTheSameCalendarDate()
    {
        var us = Session(DataSource.Yahoo, ["AAPL"]);
        var korea = Session(DataSource.LsSecurities, ["005930"]);
        _data.SetupSequence(service => service.OpenSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(us.Object)
            .ReturnsAsync(korea.Object)
            .ReturnsAsync(us.Object);
        ConfigureReady(MarketRegion.UnitedStates, new DateTime(2026, 8, 18, 17, 0, 0));
        ConfigureReady(MarketRegion.Korea, new DateTime(2026, 8, 18, 16, 30, 0));
        ConfigureSuccessfulFetch(us);
        ConfigureSuccessfulFetch(korea);

        var sut = CreateSut();
        (await sut.RunScheduledAsync()).Status.Should().Be(DailyMarketDataSyncStatus.Completed);
        (await sut.RunScheduledAsync()).Status.Should().Be(DailyMarketDataSyncStatus.Completed);
        (await sut.RunScheduledAsync()).Status.Should().Be(DailyMarketDataSyncStatus.AlreadyCompleted);

        us.Verify(service => service.RefreshStatisticsAsync(It.IsAny<CancellationToken>()), Times.Once);
        korea.Verify(service => service.RefreshStatisticsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PartialFailureRemainsEligibleForTheNextCycle()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        ConfigureReady(MarketRegion.UnitedStates, new DateTime(2026, 8, 18, 17, 0, 0));
        _session.Setup(service => service.GetLastStoredBarAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);
        _session.SetupSequence(service => service.FetchBarsAsync(
                "AAPL", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary provider failure"))
            .ReturnsAsync(Bars("AAPL", 1));
        _session.Setup(service => service.FetchBarsAsync(
                DataProviderCatalog.UnitedStatesRegimeBenchmark,
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bars(DataProviderCatalog.UnitedStatesRegimeBenchmark, 1));

        var sut = CreateSut();
        var first = await sut.RunScheduledAsync();
        var second = await sut.RunScheduledAsync();
        var third = await sut.RunScheduledAsync();

        first.Status.Should().Be(DailyMarketDataSyncStatus.PartiallyFailed);
        first.FailedSymbols.Should().Be(1);
        second.Status.Should().Be(DailyMarketDataSyncStatus.Completed);
        third.Status.Should().Be(DailyMarketDataSyncStatus.AlreadyCompleted);
        _session.Verify(service => service.FetchBarsAsync(
            "AAPL", It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _session.Verify(service => service.RefreshStatisticsAsync(
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task InitialSyncFetchesOnlySymbolsBelowTheirCentralMinimum()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        ConfigureReady(MarketRegion.UnitedStates, new DateTime(2026, 8, 18, 17, 0, 0));
        _session.Setup(service => service.LoadStoredBarsAsync(
                "AAPL", It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bars("AAPL", StrategyEvaluationPolicy.LiveScannerMinimumBars - 1));
        _session.Setup(service => service.LoadStoredBarsAsync(
                DataProviderCatalog.UnitedStatesRegimeBenchmark,
                It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bars(
                DataProviderCatalog.UnitedStatesRegimeBenchmark,
                StrategyEvaluationPolicy.RegimeTrendBars));
        _session.Setup(service => service.FetchBarsAsync(
                "AAPL", It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bars("AAPL", 25));

        await CreateSut().RunInitialSyncIfNeededAsync();

        _session.Verify(service => service.FetchBarsAsync(
            "AAPL", It.IsAny<DateTime>(), Observation.UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
        _session.Verify(service => service.FetchBarsAsync(
            DataProviderCatalog.UnitedStatesRegimeBenchmark,
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _session.Verify(service => service.SaveBarsAsync(
            It.Is<IReadOnlyList<OhlcvBar>>(bars => bars.Count == 25),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitialSyncDoesNotPersistTheCurrentUnfinishedDailyBar()
    {
        ConfigureSession(DataSource.Yahoo, ["AAPL"]);
        _calendar.Setup(service => service.GetLocalTime(
                MarketRegion.UnitedStates, Observation.UtcDateTime))
            .Returns(new DateTime(2026, 8, 18, 12, 0, 0));
        _calendar.Setup(service => service.GetMarketClose(MarketRegion.UnitedStates))
            .Returns(new TimeSpan(16, 0, 0));
        _session.Setup(service => service.LoadStoredBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _session.Setup(service => service.FetchBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), Observation.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string symbol, DateTime _, DateTime _, CancellationToken _) =>
            [
                Bar(symbol, new DateTime(2026, 8, 17), 100m),
                Bar(symbol, new DateTime(2026, 8, 18), 999m)
            ]);

        await CreateSut().RunInitialSyncIfNeededAsync();

        _session.Verify(service => service.SaveBarsAsync(
            It.Is<IReadOnlyList<OhlcvBar>>(bars =>
                bars.Count == 1
                && bars[0].Timestamp.Date == new DateTime(2026, 8, 17)
                && bars[0].Close == 100m),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private DailyMarketDataSyncCycle CreateSut() => new(
        _data.Object,
        _calendar.Object,
        _state,
        Options.Create(new TradingSettings()),
        new FixedTimeProvider(Observation),
        NullLogger<DailyMarketDataSyncCycle>.Instance);

    private void ConfigureSession(DataSource source, IReadOnlyList<string> symbols)
    {
        _session.SetupGet(value => value.Source).Returns(source);
        _session.SetupGet(value => value.WatchlistSymbols).Returns(symbols);
        _data.Setup(service => service.OpenSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session.Object);
    }

    private void ConfigureReady(MarketRegion market, DateTime localTime)
    {
        _calendar.Setup(service => service.GetLocalTime(market, Observation.UtcDateTime))
            .Returns(localTime);
        _calendar.Setup(service => service.GetMarketClose(market))
            .Returns(MarketRegionCatalog.Get(market).RegularClose);
    }

    private static Mock<IDailyMarketDataSyncSession> Session(
        DataSource source,
        IReadOnlyList<string> symbols)
    {
        var session = new Mock<IDailyMarketDataSyncSession>();
        session.SetupGet(value => value.Source).Returns(source);
        session.SetupGet(value => value.WatchlistSymbols).Returns(symbols);
        return session;
    }

    private static void ConfigureSuccessfulFetch(Mock<IDailyMarketDataSyncSession> session)
    {
        session.Setup(service => service.GetLastStoredBarAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);
        session.Setup(service => service.FetchBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string symbol, DateTime _, DateTime _, CancellationToken _) =>
                Bars(symbol, 1));
    }

    private static IReadOnlyList<OhlcvBar> Bars(string symbol, int count) =>
        Enumerable.Range(0, count)
            .Select(index => new OhlcvBar
            {
                Symbol = symbol,
                Timestamp = Observation.UtcDateTime.Date.AddDays(index - count),
                Open = 99m,
                High = 101m,
                Low = 98m,
                Close = 100m,
                Volume = 1_000
            })
            .ToArray();

    private static OhlcvBar Bar(string symbol, DateTime timestamp, decimal close) => new()
    {
        Symbol = symbol,
        Timestamp = timestamp,
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1_000
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

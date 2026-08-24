using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Market;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;
using StockTrader.Services.Signal;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Tests;

public sealed class LivePatternScanCycleTests
{
    private static readonly DateTimeOffset Observation =
        new(2026, 8, 18, 14, 30, 0, TimeSpan.Zero);

    private readonly Mock<ILiveDailyScanData> _data = new();
    private readonly Mock<ILiveMarketRegimeEvaluator> _regime = new();
    private readonly Mock<ILivePatternDetection> _detection = new();
    private readonly Mock<ILiveSignalProcessor> _processor = new();
    private readonly Mock<IMarketCalendar> _calendar = new();

    public LivePatternScanCycleTests()
    {
        _calendar.Setup(service => service.GetLocalTime(
                It.IsAny<MarketRegion>(), Observation.UtcDateTime))
            .Returns(new DateTime(2026, 8, 18, 10, 30, 0));
    }

    [Fact]
    public async Task InsufficientBarsDoNotConsumeTheDailyScan()
    {
        var subjectBars = Bars(StrategyEvaluationPolicy.LiveScannerMinimumBars - 1);
        ConfigureData(symbol => symbol == "AAPL" ? subjectBars : Bars(200));
        ConfigureNoSignals();

        var sut = CreateSut();
        await sut.RunAsync(" aapl ");
        subjectBars = Bars(StrategyEvaluationPolicy.LiveScannerMinimumBars);
        await sut.RunAsync("AAPL");

        _detection.Verify(service => service.ScanSymbolAsync(
            "AAPL",
            It.Is<OhlcvBar[]>(bars => bars.Length == StrategyEvaluationPolicy.LiveScannerMinimumBars),
            It.IsAny<MarketRegime>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuccessfulNoSignalScanIsDeduplicatedForTheSameMarketDate()
    {
        ConfigureData(_ => Bars(StrategyEvaluationPolicy.RegimeTrendBars));
        ConfigureNoSignals();

        var sut = CreateSut();
        await sut.RunAsync("AAPL");
        await sut.RunAsync("aapl");

        _detection.Verify(service => service.ScanSymbolAsync(
            "AAPL", It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _data.Verify(service => service.ResolveContextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RegimeIsSharedAcrossSymbolsForTheSameBenchmarkAndDate()
    {
        ConfigureData(_ => Bars(StrategyEvaluationPolicy.RegimeTrendBars));
        ConfigureNoSignals();

        var sut = CreateSut();
        await sut.RunAsync("AAPL");
        await sut.RunAsync("MSFT");

        _regime.Verify(service => service.Evaluate(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), Observation.UtcDateTime), Times.Once);
        _detection.Verify(service => service.ScanSymbolAsync(
            It.IsAny<string>(), It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BenchmarkChangeInvalidatesTheDailyRegimeCache()
    {
        _data.SetupSequence(service => service.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.Yahoo, MarketRegion.UnitedStates, "SPY"))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.Alpaca, MarketRegion.UnitedStates, "QQQ"));
        _data.Setup(service => service.LoadBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BarSet(Bars(StrategyEvaluationPolicy.RegimeTrendBars)));
        ConfigureNoSignals();

        var sut = CreateSut();
        await sut.RunAsync("AAPL");
        await sut.RunAsync("MSFT");

        _regime.Verify(service => service.Evaluate(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), Observation.UtcDateTime), Times.Exactly(2));
        _data.Verify(service => service.LoadBarsAsync(
            "SPY", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _data.Verify(service => service.LoadBarsAsync(
            "QQQ", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProviderCompletionIsIndependentAndUsesItsOwnedMarketDate()
    {
        _data.SetupSequence(service => service.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.Yahoo, MarketRegion.UnitedStates, "SPY"))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.LsSecurities, MarketRegion.Korea, "069500"))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.Yahoo, MarketRegion.UnitedStates, "SPY"));
        _data.Setup(service => service.LoadBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BarSet(Bars(StrategyEvaluationPolicy.RegimeTrendBars)));
        ConfigureNoSignals();

        var sut = CreateSut();
        await sut.RunAsync("AAPL");
        await sut.RunAsync("AAPL");
        await sut.RunAsync("AAPL");

        _detection.Verify(service => service.ScanSymbolAsync(
            "AAPL", It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _calendar.Verify(service => service.GetLocalTime(
            MarketRegion.UnitedStates, Observation.UtcDateTime), Times.Exactly(2));
        _calendar.Verify(service => service.GetLocalTime(
            MarketRegion.Korea, Observation.UtcDateTime), Times.Once);
    }

    [Fact]
    public async Task FailedSignalProcessingDoesNotConsumeTheDailyScan()
    {
        ConfigureData(_ => Bars(StrategyEvaluationPolicy.RegimeTrendBars));
        _regime.Setup(service => service.Evaluate(
                It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<DateTime>()))
            .Returns(new MarketRegime { RegimeLabel = "강세" });
        var signal = new PatternSignal
        {
            Symbol = "AAPL",
            PatternType = PatternType.Breakout,
            SignalBarAt = Observation.UtcDateTime.Date
        };
        _detection.Setup(service => service.ScanSymbolAsync(
                "AAPL", It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([signal]);
        _processor.SetupSequence(service => service.ProcessAsync(
                It.IsAny<IReadOnlyList<PatternSignal>>(), It.IsAny<MarketDataEvidenceContract>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary failure"))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var first = () => sut.RunAsync("AAPL");
        await first.Should().ThrowAsync<InvalidOperationException>();
        await sut.RunAsync("AAPL");
        await sut.RunAsync("AAPL");

        _processor.Verify(service => service.ProcessAsync(
            It.Is<IReadOnlyList<PatternSignal>>(signals => signals.Single() == signal),
            It.IsAny<MarketDataEvidenceContract>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _detection.Verify(service => service.ScanSymbolAsync(
            "AAPL", It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _regime.Verify(service => service.Evaluate(
            It.IsAny<IReadOnlyList<OhlcvBar>>(), Observation.UtcDateTime), Times.Once);
    }

    private LivePatternScanCycle CreateSut()
    {
        var timeProvider = new FixedTimeProvider(Observation);
        return new LivePatternScanCycle(
            _data.Object,
            _regime.Object,
            _detection.Object,
            _processor.Object,
            new LivePatternScanState(),
            _calendar.Object,
            timeProvider,
            NullLogger<LivePatternScanCycle>.Instance);
    }

    private void ConfigureData(Func<string, IReadOnlyList<OhlcvBar>> bars)
    {
        _data.Setup(service => service.ResolveContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveDailyScanContext(
                DataSource.Yahoo, MarketRegion.UnitedStates, "SPY"));
        _data.Setup(service => service.LoadBarsAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string symbol, DateTime _, DateTime _, CancellationToken _) =>
                BarSet(bars(symbol)));
    }

    private void ConfigureNoSignals()
    {
        _regime.Setup(service => service.Evaluate(
                It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<DateTime>()))
            .Returns(new MarketRegime { RegimeLabel = "강세" });
        _detection.Setup(service => service.ScanSymbolAsync(
                It.IsAny<string>(), It.IsAny<OhlcvBar[]>(), It.IsAny<MarketRegime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private static IReadOnlyList<OhlcvBar> Bars(int count) => Enumerable.Range(0, count)
        .Select(index => new OhlcvBar
        {
            Symbol = "TEST",
            Timestamp = Observation.UtcDateTime.Date.AddDays(index - count),
            Open = 99m + index,
            High = 101m + index,
            Low = 98m + index,
            Close = 100m + index,
            Volume = 1_000
        })
        .ToArray();

    private static LiveDailyBarSet BarSet(IReadOnlyList<OhlcvBar> bars) =>
        new(bars, Evidence());

    private static MarketDataEvidenceContract Evidence() => new(
        1, "evidence", "Yahoo", "TEST", "Daily", "Raw", "US",
        "calendar-v1", DateTime.UnixEpoch, Observation.UtcDateTime,
        DateTime.UnixEpoch, Observation.UtcDateTime, 1, true, "content");

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

public sealed class LiveMarketRegimeEvaluatorTests
{
    [Fact]
    public void InsufficientBarsReturnUnknownWithoutCalculatingTrend()
    {
        var observedAt = new DateTime(2026, 8, 18, 14, 30, 0, DateTimeKind.Utc);
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars - 1, 100m);

        var result = new LiveMarketRegimeEvaluator().Evaluate(bars, observedAt);

        result.RegimeLabel.Should().Be(MarketRegimeTrendPolicy.UnknownLabel);
        result.SpyAbove200Ma.Should().BeFalse();
        result.AsOf.Should().Be(bars[^1].Timestamp);
    }

    [Theory]
    [InlineData(101, true, "강세")]
    [InlineData(99, false, "약세")]
    [InlineData(100, false, "약세")]
    public void CompletedTrendClassifiesTheLatestClose(
        int latestClose,
        bool expectedAbove,
        string expectedLabel)
    {
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars, latestClose);

        var result = new LiveMarketRegimeEvaluator()
            .Evaluate(bars, bars[^1].Timestamp);

        result.SpyPrice.Should().Be(latestClose);
        result.Spy200Ma.Should().Be(
            bars.TakeLast(StrategyEvaluationPolicy.RegimeTrendBars).Average(bar => bar.Close));
        result.SpyAbove200Ma.Should().Be(expectedAbove);
        result.RegimeLabel.Should().Be(expectedLabel);
    }

    private static IReadOnlyList<OhlcvBar> Bars(int count, decimal latestClose) =>
        Enumerable.Range(0, count)
            .Select(index => new OhlcvBar
            {
                Timestamp = DateTime.UnixEpoch.AddDays(index),
                Close = index == count - 1 ? latestClose : 100m
            })
            .ToArray();
}

public sealed class LiveSignalProcessorTests
{
    [Fact]
    public async Task PersistsBeforeEvaluationAndSubmitsEveryRecommendationInOrder()
    {
        var steps = new List<string>();
        var signals = new Mock<IPatternSignalStore>();
        var recommendations = new Mock<ISignalService>();
        var orders = new Mock<IOrderService>();
        var detected = new List<PatternSignal>
        {
            new() { Symbol = "AAPL", PatternType = PatternType.Breakout }
        };
        var first = new TradeRecommendation { Symbol = "AAPL" };
        var second = new TradeRecommendation { Symbol = "MSFT" };
        signals.Setup(store => store.AddSignalsBatchAsync(
                It.IsAny<IEnumerable<PatternSignal>>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                steps.Add("persist");
                detected[0].Id = 17;
            })
            .Returns(Task.CompletedTask);
        recommendations.Setup(service => service.EvaluateSignalsAsync(
                detected, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                detected[0].Id.Should().Be(17);
                steps.Add("evaluate");
            })
            .ReturnsAsync([first, second]);
        orders.Setup(service => service.PlaceOrderAsync(
                It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .Callback<TradeRecommendation, CancellationToken>((item, _) =>
                steps.Add($"order:{item.Symbol}"))
            .ReturnsAsync(true);

        var evidence = Evidence();
        await new LiveSignalProcessor(signals.Object, recommendations.Object, orders.Object)
            .ProcessAsync(detected, evidence);

        steps.Should().Equal("persist", "evaluate", "order:AAPL", "order:MSFT");
        first.MarketDataEvidence.Should().BeSameAs(evidence);
        second.MarketDataEvidence.Should().BeSameAs(evidence);
    }

    [Fact]
    public async Task EmptyDetectionDoesNotTouchPersistenceOrExecution()
    {
        var signals = new Mock<IPatternSignalStore>();
        var recommendations = new Mock<ISignalService>();
        var orders = new Mock<IOrderService>();

        await new LiveSignalProcessor(signals.Object, recommendations.Object, orders.Object)
            .ProcessAsync([], Evidence());

        signals.VerifyNoOtherCalls();
        recommendations.VerifyNoOtherCalls();
        orders.VerifyNoOtherCalls();
    }

    private static MarketDataEvidenceContract Evidence() => new(
        1, "evidence", "Yahoo", "TEST", "Daily", "Raw", "US",
        "calendar-v1", DateTime.UnixEpoch, DateTime.UtcNow,
        DateTime.UnixEpoch, DateTime.UtcNow, 1, true, "content");
}

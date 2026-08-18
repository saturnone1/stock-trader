using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Notification;
using StockTrader.Services.Streaming;

namespace StockTrader.Tests;

public sealed class RealtimeBarIngestionBufferTests
{
    private readonly Mock<IRealtimeBarBatchSink> _sink = new();
    private readonly Mock<IStreamingStatusService> _status = new();
    private readonly Mock<INotificationService> _notifications = new();

    [Fact]
    public async Task AcceptedBarPublishesUiImmediatelyButScannerOnlyAfterPersistence()
    {
        var bar = Bar("AAPL", 101m);
        IReadOnlyList<OhlcvBar>? persisted = null;
        _sink.Setup(service => service.PersistAndPublishAsync(
                It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<OhlcvBar> bars, CancellationToken _) =>
                persisted = bars.ToArray())
            .Returns(Task.CompletedTask);
        var sut = CreateSut();
        sut.StartAccepting();

        await sut.ProcessAsync(bar);

        persisted.Should().BeNull();
        _status.Verify(service => service.MarkActive(), Times.Once);
        _notifications.Verify(service => service.PublishPriceUpdate(
            It.Is<PriceUpdate>(update =>
                update.Symbol == "AAPL" && update.Price == 101m)), Times.Once);
        _notifications.Verify(service => service.PublishBarUpdate("AAPL"), Times.Once);

        (await sut.FlushAsync()).Should().BeTrue();
        persisted.Should().Equal(bar);
    }

    [Fact]
    public async Task FailedFlushRetainsTheExactBatchForRetry()
    {
        var bar = Bar("TQQQ", 55m);
        var newerBar = Bar("TQQQ", 56m);
        newerBar.Timestamp = bar.Timestamp.AddMinutes(1);
        var attempts = new List<IReadOnlyList<OhlcvBar>>();
        _sink.Setup(service => service.PersistAndPublishAsync(
                It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<OhlcvBar> bars, CancellationToken _) =>
                attempts.Add(bars.ToArray()))
            .Returns(() => attempts.Count == 1
                ? Task.FromException(new InvalidOperationException("database unavailable"))
                : Task.CompletedTask);
        var sut = CreateSut();
        sut.StartAccepting();
        await sut.ProcessAsync(bar);

        (await sut.FlushAsync()).Should().BeFalse();
        await sut.ProcessAsync(newerBar);
        (await sut.FlushAsync()).Should().BeTrue();

        attempts.Should().HaveCount(3);
        attempts.Take(2).Should().OnlyContain(bars =>
            bars.Count == 1 && ReferenceEquals(bars[0], bar));
        attempts[2].Should().ContainSingle().Which.Should().BeSameAs(newerBar);
    }

    [Fact]
    public async Task StopBoundaryRejectsCallbacksThatArriveAfterProviderHandoff()
    {
        var accepted = Bar("AAPL", 100m);
        var rejected = Bar("AAPL", 200m);
        IReadOnlyList<OhlcvBar>? persisted = null;
        _sink.Setup(service => service.PersistAndPublishAsync(
                It.IsAny<IReadOnlyList<OhlcvBar>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<OhlcvBar> bars, CancellationToken _) =>
                persisted = bars.ToArray())
            .Returns(Task.CompletedTask);
        var sut = CreateSut();
        sut.StartAccepting();
        await sut.ProcessAsync(accepted);

        await sut.StopAcceptingAsync();
        await sut.ProcessAsync(rejected);
        await sut.FlushAsync();

        persisted.Should().ContainSingle().Which.Close.Should().Be(100m);
        _notifications.Verify(service => service.PublishPriceUpdate(
            It.Is<PriceUpdate>(update => update.Price == 200m)), Times.Never);
    }

    private RealtimeBarIngestionBuffer CreateSut() => new(
        _sink.Object,
        _status.Object,
        _notifications.Object,
        Options.Create(new StreamingSettings
        {
            BufferCapacity = 100,
            BarFlushIntervalSeconds = 5
        }),
        TimeProvider.System,
        NullLogger<RealtimeBarIngestionBuffer>.Instance);

    private static OhlcvBar Bar(string symbol, decimal close) => new()
    {
        Symbol = symbol,
        TimeFrame = TimeFrame.OneMinute,
        Timestamp = new DateTime(2026, 8, 19, 14, 0, 0, DateTimeKind.Utc),
        Open = close - 1,
        High = close + 1,
        Low = close - 2,
        Close = close,
        Volume = 1_000
    };
}

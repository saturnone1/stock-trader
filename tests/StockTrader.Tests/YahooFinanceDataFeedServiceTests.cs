using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Market;

namespace StockTrader.Tests;

public sealed class YahooFinanceDataFeedServiceTests
{
    [Fact]
    public async Task GetHistoricalBarsAsync_PreservesAnExplicitUtcRequestWindow()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com")
        };
        using var service = CreateService(client);
        var from = new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero);

        await service.GetHistoricalBarsAsync(
            "TQQQ",
            Domain.MarketData.TimeFrame.OneMinute,
            from.UtcDateTime,
            to.UtcDateTime);

        handler.RequestUri.Should().NotBeNull();
        handler.RequestUri!.Query.Should().Contain($"period1={from.ToUnixTimeSeconds()}");
        handler.RequestUri.Query.Should().Contain($"period2={to.ToUnixTimeSeconds()}");
    }

    [Theory]
    [InlineData(2026, 1, 15, 14, 30, 21, 0)]
    [InlineData(2026, 7, 15, 13, 30, 20, 0)]
    public async Task GetIntradayBarsAsync_RequestsTheExactRegularSessionUtcWindow(
        int year,
        int month,
        int day,
        int expectedStartHourUtc,
        int expectedStartMinuteUtc,
        int expectedEndHourUtc,
        int expectedEndMinuteUtc)
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com")
        };
        using var service = CreateService(client);

        await service.GetIntradayBarsAsync("TQQQ", new DateTime(year, month, day));

        var expectedStart = new DateTimeOffset(
            year, month, day, expectedStartHourUtc, expectedStartMinuteUtc, 0, TimeSpan.Zero)
            .ToUnixTimeSeconds();
        var expectedEnd = new DateTimeOffset(
            year, month, day, expectedEndHourUtc, expectedEndMinuteUtc, 0, TimeSpan.Zero)
            .ToUnixTimeSeconds();
        handler.RequestUri.Should().NotBeNull();
        handler.RequestUri!.Query.Should().Contain($"period1={expectedStart}");
        handler.RequestUri.Query.Should().Contain($"period2={expectedEnd}");
        handler.RequestUri.Query.Should().Contain("interval=1m");
    }

    private static YahooFinanceDataFeedService CreateService(HttpClient client) => new(
        client,
        Options.Create(new YahooFinanceSettings { RateLimitDelayMs = 0 }),
        new MarketCalendar(TimeProvider.System),
        NullLogger<YahooFinanceDataFeedService>.Instance);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"chart\":{\"result\":[]}}")
            });
        }
    }
}

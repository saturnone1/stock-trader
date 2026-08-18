using FluentAssertions;
using Moq;
using StockTrader.Application.Signals;
using StockTrader.Application.Statistics;
using StockTrader.Application.Trading;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Services.Signal;

namespace StockTrader.Tests;

public sealed class SignalListQueryTests
{
    [Fact]
    public async Task GetAsync_UsesInjectedObservationAndCentralFreshnessWindow()
    {
        var observedAt = new DateTimeOffset(2026, 8, 18, 14, 30, 0, TimeSpan.Zero);
        var from = observedAt.UtcDateTime.AddHours(-12);
        var store = new Mock<IPatternSignalStore>();
        store.Setup(item => item.GetActionableSignalsAsync(
                from,
                observedAt.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PatternSignal
            {
                Id = 7,
                Symbol = "TQQQ",
                PatternType = PatternType.Breakout,
                EntryPrice = 100m,
                StopLossPrice = 95m,
                TargetPrice = 110m,
                DetectedAt = observedAt.UtcDateTime,
                IsActive = true,
            }]);
        var statistics = new Mock<IPatternStatisticsQuery>();
        statistics.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var query = new SignalListQuery(
            store.Object,
            statistics.Object,
            new SignalFreshnessPolicy(TimeSpan.FromHours(12)),
            new FixedTimeProvider(observedAt));

        var result = await query.GetAsync(new SignalBrowseRequest(null, null, null, null));

        result.Count.Should().Be(1);
        store.VerifyAll();
    }

    private sealed class FixedTimeProvider(DateTimeOffset observedAt) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => observedAt;
    }
}

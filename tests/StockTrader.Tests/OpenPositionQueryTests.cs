using FluentAssertions;
using Moq;
using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Portfolio;

namespace StockTrader.Tests;

public class OpenPositionQueryTests
{
    [Fact]
    public async Task GetAsync_UsesOneObservationTimeForEveryPositionAndTotal()
    {
        var observedAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var clock = new CountingTimeProvider(observedAt);
        var positions = new Mock<IOpenPositionStore>();
        positions.Setup(store => store.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new()
                {
                    Id = 1,
                    Symbol = "TQQQ",
                    Quantity = 10,
                    EntryPrice = 100m,
                    CurrentPrice = 105m,
                    OpenedAt = observedAt.UtcDateTime.AddDays(-3),
                    ExecutionRequestedAt = observedAt.UtcDateTime.AddMinutes(-2),
                    ExecutionRequestKind = PositionExecutionKind.ScaleOut,
                    ExecutionRequestQuantity = 3,
                    ExecutionRequestReason = "risk reduction"
                },
                new()
                {
                    Id = 2,
                    Symbol = "AAPL",
                    Quantity = 5,
                    EntryPrice = 200m,
                    CurrentPrice = 198m,
                    OpenedAt = observedAt.UtcDateTime.AddHours(2)
                }
            });
        var sut = new OpenPositionQuery(positions.Object, clock);

        var result = await sut.GetAsync();

        clock.GetUtcNowCalls.Should().Be(1);
        result.Count.Should().Be(2);
        result.TotalUnrealizedPnL.Should().Be(40m);
        result.ObservedAt.Should().Be(observedAt.UtcDateTime);
        result.Positions[0].HoldingDays.Should().Be(3);
        result.Positions[0].OrderStatus.Should().Be("SubmissionUnconfirmed");
        result.Positions[0].OrderKind.Should().Be("ScaleOut");
        result.Positions[0].OrderPendingSeconds.Should().Be(120);
        result.Positions[0].OrderQuantity.Should().Be(3);
        result.Positions[1].HoldingDays.Should().Be(0);
        result.Positions[1].OrderStatus.Should().Be("Ready");
    }

    private sealed class CountingTimeProvider(DateTimeOffset observedAt) : TimeProvider
    {
        public int GetUtcNowCalls { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCalls++;
            return observedAt;
        }
    }
}

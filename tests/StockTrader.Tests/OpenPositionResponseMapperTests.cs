using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OpenPositionResponseMapperTests
{
    [Fact]
    public void Map_ExposesPendingPositionOrderWithoutBrokerOrderIdentifier()
    {
        var now = new DateTime(2026, 8, 18, 14, 5, 0, DateTimeKind.Utc);
        var position = new Position
        {
            Id = 7,
            Symbol = "TQQQ",
            OpenedAt = now.AddDays(-3),
            Quantity = 10,
            ExecutionRequestedAt = now.AddMinutes(-2),
            ExecutionRequestReason = "사용자 수동 청산",
            ExecutionRequestQuantity = 10,
        };

        var response = OpenPositionResponseMapper.Map(position, now);

        response.OrderStatus.Should().Be("SubmissionUnconfirmed");
        response.OrderPendingSeconds.Should().Be(120);
        response.OrderReason.Should().Be("사용자 수동 청산");
        response.OrderKind.Should().Be("FullExit");
        response.HasBrokerOrderId.Should().BeFalse();
        response.OrderQuantity.Should().Be(10);
        response.OrderMarksPartialProfit.Should().BeFalse();
        response.HoldingDays.Should().Be(3);
    }
}

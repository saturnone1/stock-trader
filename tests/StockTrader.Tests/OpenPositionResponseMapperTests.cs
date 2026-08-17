using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Models;

namespace StockTrader.Tests;

public class OpenPositionResponseMapperTests
{
    [Fact]
    public void Map_ExposesPendingExitWithoutBrokerOrderIdentifier()
    {
        var now = new DateTime(2026, 8, 18, 14, 5, 0, DateTimeKind.Utc);
        var position = new Position
        {
            Id = 7,
            Symbol = "TQQQ",
            OpenedAt = now.AddDays(-3),
            ExitRequestedAt = now.AddMinutes(-2),
            ExitRequestReason = "사용자 수동 청산"
        };

        var response = OpenPositionResponseMapper.Map(position, now);

        response.ExitStatus.Should().Be("SubmissionUnconfirmed");
        response.ExitPendingSeconds.Should().Be(120);
        response.ExitRequestReason.Should().Be("사용자 수동 청산");
        response.HasExitOrderId.Should().BeFalse();
        response.HoldingDays.Should().Be(3);
    }
}

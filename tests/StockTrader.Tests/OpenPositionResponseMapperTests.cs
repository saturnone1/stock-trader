using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Portfolio;

namespace StockTrader.Tests;

public class OpenPositionResponseMapperTests
{
    [Fact]
    public void Map_PreservesApplicationPositionProjectionAndFormatsTimestamps()
    {
        var now = new DateTime(2026, 8, 18, 14, 5, 0, DateTimeKind.Utc);
        var position = new OpenPositionSnapshot(
            Id: 7,
            Symbol: "TQQQ",
            Sector: "ETF",
            Quantity: 10,
            EntryPrice: 100m,
            CurrentPrice: 105m,
            StopLossPrice: 97m,
            TargetPrice: 110m,
            Pattern: "Breakout",
            UnrealizedPnL: 50m,
            AccountId: 1,
            HighSinceEntry: 106m,
            EntryAtr: 2m,
            HoldingDays: 3,
            OpenedAt: now.AddDays(-3),
            OrderStatus: "SubmissionUnconfirmed",
            OrderRequestedAt: now.AddMinutes(-2),
            OrderReason: "사용자 수동 청산",
            OrderKind: "FullExit",
            HasBrokerOrderId: false,
            OrderPendingSeconds: 120,
            OrderQuantity: 10,
            OrderMarksPartialProfit: false);

        var response = OpenPositionResponseMapper.Map(position);

        response.OrderStatus.Should().Be("SubmissionUnconfirmed");
        response.OrderPendingSeconds.Should().Be(120);
        response.OrderReason.Should().Be("사용자 수동 청산");
        response.OrderKind.Should().Be("FullExit");
        response.HasBrokerOrderId.Should().BeFalse();
        response.OrderQuantity.Should().Be(10);
        response.OrderMarksPartialProfit.Should().BeFalse();
        response.HoldingDays.Should().Be(3);
        response.OpenedAt.Should().Be(now.AddDays(-3).ToString("o"));
        response.OrderRequestedAt.Should().Be(now.AddMinutes(-2).ToString("o"));
    }
}

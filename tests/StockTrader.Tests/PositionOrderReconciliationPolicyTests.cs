using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class PositionOrderReconciliationPolicyTests
{
    private static readonly DateTime RequestedAt = new(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_FinalizesOnlyMatchingFilledSellOrderWithPrice()
    {
        var result = PositionOrderReconciliationPolicy.Resolve(
            "TQQQ", "exit-1", RequestedAt, TradeDirection.Short, [
            Order("entry", TradeDirection.Long, BrokerOrderStatus.Filled, 50m),
            Order("exit-1", TradeDirection.Short, BrokerOrderStatus.Filled, 48m),
        ]);

        result.Action.Should().Be(PositionOrderReconciliationAction.Finalize);
        result.Order!.AverageFillPrice.Should().Be(48m);
    }

    [Theory]
    [InlineData(BrokerOrderStatus.Pending)]
    [InlineData(BrokerOrderStatus.Accepted)]
    [InlineData(BrokerOrderStatus.PartiallyFilled)]
    public void Resolve_DoesNotResubmitNonTerminalOrder(BrokerOrderStatus status)
    {
        PositionOrderReconciliationPolicy.Resolve(
            "TQQQ", "exit-1", RequestedAt, TradeDirection.Short,
            [Order("exit-1", TradeDirection.Short, status, null)])
            .Action.Should().Be(PositionOrderReconciliationAction.Wait);
    }

    [Theory]
    [InlineData(BrokerOrderStatus.Cancelled)]
    [InlineData(BrokerOrderStatus.Rejected)]
    [InlineData(BrokerOrderStatus.Expired)]
    public void Resolve_ReleasesOnlyTerminalFailedOrder(BrokerOrderStatus status)
    {
        PositionOrderReconciliationPolicy.Resolve(
            "TQQQ", "exit-1", RequestedAt, TradeDirection.Short,
            [Order("exit-1", TradeDirection.Short, status, null)])
            .Action.Should().Be(PositionOrderReconciliationAction.ReleaseForRetry);
    }

    [Fact]
    public void Resolve_WaitsWhenOrderHistoryCannotProveOutcome()
    {
        PositionOrderReconciliationPolicy.Resolve(
            "TQQQ", "exit-1", RequestedAt, TradeDirection.Short, [])
            .Action.Should().Be(PositionOrderReconciliationAction.Wait);
    }

    [Fact]
    public void Resolve_DoesNotMatchOlderOrDifferentOrderWhenIdIsKnown()
    {
        var olderExpectedOrder = Order(
            "exit-expected", TradeDirection.Short, BrokerOrderStatus.Filled, 47m);
        olderExpectedOrder.SubmittedAt = RequestedAt.AddMinutes(-1);
        var result = PositionOrderReconciliationPolicy.Resolve(
            "TQQQ", "exit-expected", RequestedAt, TradeDirection.Short, [
            Order("exit-other", TradeDirection.Short, BrokerOrderStatus.Filled, 48m),
            olderExpectedOrder,
        ]);

        result.Action.Should().Be(PositionOrderReconciliationAction.Wait);
    }

    private static BrokerOrder Order(
        string id, TradeDirection direction, BrokerOrderStatus status, decimal? fillPrice) => new()
    {
        OrderId = id,
        Symbol = "TQQQ",
        Direction = direction,
        Status = status,
        AverageFillPrice = fillPrice,
        SubmittedAt = RequestedAt,
        FilledAt = status == BrokerOrderStatus.Filled ? RequestedAt.AddSeconds(1) : null,
    };
}

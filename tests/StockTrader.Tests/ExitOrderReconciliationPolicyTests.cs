using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class ExitOrderReconciliationPolicyTests
{
    private static readonly DateTime RequestedAt = new(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_FinalizesOnlyMatchingFilledSellOrderWithPrice()
    {
        var result = ExitOrderReconciliationPolicy.Resolve("TQQQ", "exit-1", RequestedAt, [
            Order("entry", TradeDirection.Long, BrokerOrderStatus.Filled, 50m),
            Order("exit-1", TradeDirection.Short, BrokerOrderStatus.Filled, 48m),
        ]);

        result.Action.Should().Be(ExitOrderReconciliationAction.Finalize);
        result.Order!.AverageFillPrice.Should().Be(48m);
    }

    [Theory]
    [InlineData(BrokerOrderStatus.Pending)]
    [InlineData(BrokerOrderStatus.Accepted)]
    [InlineData(BrokerOrderStatus.PartiallyFilled)]
    public void Resolve_DoesNotResubmitNonTerminalOrder(BrokerOrderStatus status)
    {
        ExitOrderReconciliationPolicy.Resolve("TQQQ", "exit-1", RequestedAt,
            [Order("exit-1", TradeDirection.Short, status, null)])
            .Action.Should().Be(ExitOrderReconciliationAction.Wait);
    }

    [Theory]
    [InlineData(BrokerOrderStatus.Cancelled)]
    [InlineData(BrokerOrderStatus.Rejected)]
    [InlineData(BrokerOrderStatus.Expired)]
    public void Resolve_ReleasesOnlyTerminalFailedOrder(BrokerOrderStatus status)
    {
        ExitOrderReconciliationPolicy.Resolve("TQQQ", "exit-1", RequestedAt,
            [Order("exit-1", TradeDirection.Short, status, null)])
            .Action.Should().Be(ExitOrderReconciliationAction.ReleaseForRetry);
    }

    [Fact]
    public void Resolve_WaitsWhenOrderHistoryCannotProveOutcome()
    {
        ExitOrderReconciliationPolicy.Resolve("TQQQ", "exit-1", RequestedAt, [])
            .Action.Should().Be(ExitOrderReconciliationAction.Wait);
    }

    [Fact]
    public void Resolve_DoesNotMatchOlderOrDifferentOrderWhenIdIsKnown()
    {
        var olderExpectedOrder = Order(
            "exit-expected", TradeDirection.Short, BrokerOrderStatus.Filled, 47m);
        olderExpectedOrder.SubmittedAt = RequestedAt.AddMinutes(-1);
        var result = ExitOrderReconciliationPolicy.Resolve("TQQQ", "exit-expected", RequestedAt, [
            Order("exit-other", TradeDirection.Short, BrokerOrderStatus.Filled, 48m),
            olderExpectedOrder,
        ]);

        result.Action.Should().Be(ExitOrderReconciliationAction.Wait);
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

using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class LivePositionOrderStatusPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 14, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_ReturnsReadyWithoutPendingOrder()
    {
        LivePositionOrderStatusPolicy.Evaluate(new Position(), Now).Should().Be(
            new LivePositionOrderStatus(
                LivePositionOrderState.Ready, null, null, null, false, 0, 0, false));
    }

    [Fact]
    public void Evaluate_DistinguishesMissingOrderIdFromBrokerPendingOrder()
    {
        var position = new Position
        {
            Quantity = 10,
            ExecutionRequestedAt = Now.AddMinutes(-2),
            ExecutionRequestReason = "목표 도달",
            ExecutionRequestQuantity = 4,
            ExecutionRequestMarksPartialProfit = true,
        };

        var unconfirmed = LivePositionOrderStatusPolicy.Evaluate(position, Now);
        unconfirmed.State.Should().Be(LivePositionOrderState.SubmissionUnconfirmed);
        unconfirmed.Kind.Should().Be(PositionExecutionKind.FullExit);
        unconfirmed.PendingSeconds.Should().Be(120);
        unconfirmed.RequestedQuantity.Should().Be(4);
        unconfirmed.MarksPartialProfit.Should().BeTrue();

        position.ExecutionOrderId = "position-order-1";
        var pending = LivePositionOrderStatusPolicy.Evaluate(position, Now);
        pending.State.Should().Be(LivePositionOrderState.AwaitingBroker);
        pending.HasBrokerOrderId.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ExposesNonExitPositionOrderKind()
    {
        var position = new Position
        {
            Quantity = 10,
            ExecutionRequestedAt = Now.AddSeconds(-30),
            ExecutionRequestKind = PositionExecutionKind.ScaleIn,
            ExecutionRequestQuantity = 2,
        };

        var status = LivePositionOrderStatusPolicy.Evaluate(position, Now);

        status.Kind.Should().Be(PositionExecutionKind.ScaleIn);
        status.RequestedQuantity.Should().Be(2);
    }
}

using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LiveExitIntentStatusPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 14, 5, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_ReturnsReadyWithoutExitIntent()
    {
        LiveExitIntentStatusPolicy.Evaluate(new Position(), Now).Should().Be(
            new LiveExitIntentStatus(LiveExitIntentState.Ready, null, null, false, 0));
    }

    [Fact]
    public void Evaluate_DistinguishesMissingOrderIdFromBrokerPendingOrder()
    {
        var position = new Position
        {
            ExitRequestedAt = Now.AddMinutes(-2),
            ExitRequestReason = "목표 도달"
        };

        var unconfirmed = LiveExitIntentStatusPolicy.Evaluate(position, Now);
        unconfirmed.State.Should().Be(LiveExitIntentState.SubmissionUnconfirmed);
        unconfirmed.PendingSeconds.Should().Be(120);

        position.ExitOrderId = "exit-1";
        var pending = LiveExitIntentStatusPolicy.Evaluate(position, Now);
        pending.State.Should().Be(LiveExitIntentState.AwaitingBroker);
        pending.HasBrokerOrderId.Should().BeTrue();
    }
}

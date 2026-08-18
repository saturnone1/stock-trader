using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class LiveEntryOrderStatusPolicyTests
{
    private static readonly DateTime Now =
        new(2026, 8, 18, 15, 5, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, LiveEntryOrderState.SubmissionUnconfirmed)]
    [InlineData("entry-1", LiveEntryOrderState.AwaitingBroker)]
    public void PendingStateDistinguishesMissingAndPersistedBrokerEvidence(
        string? orderId,
        LiveEntryOrderState expected)
    {
        var recommendation = new TradeRecommendation
        {
            EntryRequestedAt = Now.AddMinutes(-2),
            EntryAccountId = 7,
            EntryOrderId = orderId,
        };

        var status = LiveEntryOrderStatusPolicy.Evaluate(recommendation, Now);

        status.State.Should().Be(expected);
        status.PendingSeconds.Should().Be(120);
        status.AccountId.Should().Be(7);
        status.HasBrokerOrderId.Should().Be(orderId is not null);
    }

    [Fact]
    public void ReleasedFailureRetainsAuditEvidenceWithoutLookingPending()
    {
        var recommendation = new TradeRecommendation
        {
            EntryAccountId = 7,
            EntryOrderId = "entry-1",
            EntryExecutionNote = "Broker returned terminal status Rejected.",
        };

        var status = LiveEntryOrderStatusPolicy.Evaluate(recommendation, Now);

        status.State.Should().Be(LiveEntryOrderState.Failed);
        status.HasBrokerOrderId.Should().BeTrue();
        status.Note.Should().Contain("Rejected");
    }
}

using FluentAssertions;
using StockTrader.Application.Signals;

namespace StockTrader.Tests;

public sealed class SignalFreshnessPolicyTests
{
    private static readonly DateTime ObservedAt =
        new(2026, 8, 18, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_UsesOneClosedWindowAndRejectsFutureObservations()
    {
        var policy = new SignalFreshnessPolicy(TimeSpan.FromHours(24));

        policy.Evaluate(ObservedAt, ObservedAt)
            .Should().Be(SignalFreshnessStatus.Actionable);
        policy.Evaluate(ObservedAt.AddHours(-24), ObservedAt)
            .Should().Be(SignalFreshnessStatus.Actionable);
        policy.Evaluate(ObservedAt.AddHours(-24).AddTicks(-1), ObservedAt)
            .Should().Be(SignalFreshnessStatus.Expired);
        policy.Evaluate(ObservedAt.AddTicks(1), ObservedAt)
            .Should().Be(SignalFreshnessStatus.FutureDated);

        policy.GetWindow(ObservedAt).Should().Be(new SignalFreshnessWindow(
            ObservedAt.AddHours(-24),
            ObservedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public void Constructor_RejectsUnsafeOperationalLifetimes(double hours)
    {
        var action = () => new SignalFreshnessPolicy(TimeSpan.FromHours(hours));

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}

using FluentAssertions;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.Streaming;

namespace StockTrader.Tests;

public sealed class StreamingStatusServiceTests
{
    [Fact]
    public void ActivityUsesInjectedObservationClockAndConfiguredStalenessWindow()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
        var status = new StreamingStatusService(
            clock,
            Options.Create(new StreamingSettings
            {
                StatusStalenessSeconds = 180,
            }));

        status.MarkActive();

        status.LastBarReceivedUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
        status.IsStreamingActive.Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(180));
        status.IsStreamingActive.Should().BeTrue(
            "the configured boundary is inclusive");

        clock.Advance(TimeSpan.FromTicks(1));
        status.IsStreamingActive.Should().BeFalse();
    }

    [Fact]
    public void ReconnectAndInactiveTransitionsDoNotInventBarTimestamps()
    {
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
        var status = new StreamingStatusService(
            clock,
            Options.Create(new StreamingSettings
            {
                StatusStalenessSeconds = 30,
            }));

        status.MarkReconnecting();

        status.IsReconnecting.Should().BeTrue();
        status.IsStreamingActive.Should().BeFalse();
        status.LastBarReceivedUtc.Should().BeNull();

        status.MarkInactive();
        status.IsReconnecting.Should().BeFalse();
        status.LastBarReceivedUtc.Should().BeNull();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}

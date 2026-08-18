using FluentAssertions;
using StockTrader.Services.LsSecurities;

namespace StockTrader.Tests;

public sealed class LsOperationalTimingPolicyTests
{
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData("2026-08-18T21:00:00+00:00", "2026-08-18T21:55:00+00:00")]
    [InlineData("2026-08-18T21:58:00+00:00", "2026-08-19T21:55:00+00:00")]
    [InlineData("2026-08-19T00:00:00+00:00", "2026-08-19T21:55:00+00:00")]
    public void TokenExpiryUsesConfiguredKoreanBoundaryAndSafetyMargin(
        string observed,
        string expected)
    {
        var expiry = LsOperationalTimingPolicy.CalculateTokenExpiryUtc(
            DateTimeOffset.Parse(observed),
            LsAuthService.KstZone,
            LsOperationalTimingPolicy.DailyTokenExpiryKst,
            SafetyMargin);

        expiry.Should().Be(DateTimeOffset.Parse(expected));
    }

    [Fact]
    public void RateLimitDelayUsesExplicitObservations()
    {
        var interval = LsOperationalTimingPolicy.MinimumChartRequestInterval;

        LsOperationalTimingPolicy.CalculateRateLimitDelay(
                elapsedSincePreviousRequest: null,
                interval)
            .Should().Be(TimeSpan.Zero);
        LsOperationalTimingPolicy.CalculateRateLimitDelay(
                TimeSpan.FromMilliseconds(400),
                interval)
            .Should().Be(TimeSpan.FromMilliseconds(600));
        LsOperationalTimingPolicy.CalculateRateLimitDelay(
                TimeSpan.FromSeconds(1),
                interval)
            .Should().Be(TimeSpan.Zero);
    }
}

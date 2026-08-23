using FluentAssertions;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Tests;

public class OptimizationWorkerTransportSecurityTests
{
    private const string ValidSecret = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Options_DefaultToDisabledShadowAndValidateEnabledSecrets()
    {
        new OptimizationWorkerTransportOptions().IsValid().Should().BeTrue();
        new OptimizationWorkerTransportOptions
        {
            Enabled = true,
            SharedSecret = "too-short"
        }.IsValid().Should().BeFalse();
        new OptimizationWorkerTransportOptions
        {
            Enabled = true,
            SharedSecret = ValidSecret,
            LeaseSeconds = OptimizationWorkerTransportOptions.MinimumLeaseSeconds
        }.IsValid().Should().BeTrue();
        new OptimizationWorkerTransportOptions
        {
            LeaseTransportEnabled = true
        }.IsValid().Should().BeFalse();
    }

    [Theory]
    [InlineData(false, ValidSecret, ValidSecret, "worker-1")]
    [InlineData(true, ValidSecret, "1123456789abcdef0123456789abcdef", "worker-1")]
    [InlineData(true, ValidSecret, ValidSecret, "")]
    public void CredentialPolicy_FailsClosed(
        bool enabled,
        string expected,
        string presented,
        string workerId)
    {
        OptimizationWorkerCredentialPolicy.IsAuthorized(
                enabled, expected, presented, workerId)
            .Should().BeFalse();
    }

    [Fact]
    public void CredentialPolicy_AcceptsOnlyTheExactIndependentWorkerCredential()
    {
        OptimizationWorkerCredentialPolicy.IsAuthorized(
                true, ValidSecret, ValidSecret, "optimization-worker-pod")
            .Should().BeTrue();
        OptimizationWorkerHttpHeaders.Secret.Should().Be("X-StockTrader-Worker-Key");
        OptimizationWorkerHttpHeaders.WorkerId.Should().Be("X-StockTrader-Worker-Id");
    }
}

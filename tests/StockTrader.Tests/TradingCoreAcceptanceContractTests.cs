using FluentAssertions;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Tests;

public sealed class TradingCoreAcceptanceContractTests
{
    [Fact]
    public void PlanAndCompleteManifestAreContentAddressed()
    {
        var now = new DateTime(2025, 1, 2, 15, 0, 0, DateTimeKind.Utc);
        var plan = new ScriptedBrokerPlan(
            TradingCoreAcceptanceVersions.Current,
            TradingCoreAcceptanceScenarioCatalog.Required[0],
            Guid.NewGuid().ToString(),
            string.Empty,
            now,
            new ScriptedBrokerAccount("synthetic", "100000", "100000", "50000",
                "50000", false, now),
            [],
            []);
        plan = plan with { PlanHash = TradingCoreAcceptanceIdentity.Plan(plan) };
        TradingCoreAcceptancePolicy.PlanError(plan).Should().BeNull();

        var results = TradingCoreAcceptanceScenarioCatalog.Required.Select(code =>
            new AcceptanceScenarioResult(Guid.NewGuid().ToString(), code, "fixture", "state",
                "state", [], now, now.AddMinutes(1), true, null)).ToArray();
        var candidate = new AcceptanceManifestV1(
            TradingCoreAcceptanceVersions.Current, string.Empty, Guid.NewGuid().ToString(),
            "IsolatedAcceptance", "commit", "build", new Dictionary<string, string>(),
            new Dictionary<string, string>(), results, now, now.AddMinutes(1), true, []);
        var manifest = candidate with
        {
            ManifestId = TradingCoreAcceptanceIdentity.Manifest(candidate)
        };
        TradingCoreAcceptancePolicy.ManifestError(manifest).Should().BeNull();
    }

    [Fact]
    public void ManifestCannotClaimPassWithoutEveryRequiredScenario()
    {
        var now = DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc);
        var candidate = new AcceptanceManifestV1(
            TradingCoreAcceptanceVersions.Current, string.Empty, Guid.NewGuid().ToString(),
            "IsolatedAcceptance", "commit", "build", new Dictionary<string, string>(),
            new Dictionary<string, string>(), [], now, now, true, []);
        var manifest = candidate with
        {
            ManifestId = TradingCoreAcceptanceIdentity.Manifest(candidate)
        };
        TradingCoreAcceptancePolicy.ManifestError(manifest)
            .Should().Be("acceptance-manifest-invalid");
    }
}

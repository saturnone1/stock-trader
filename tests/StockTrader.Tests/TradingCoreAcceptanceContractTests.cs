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
            "IsolatedAcceptance", "commit", "build", Hashes(
                TradingCoreAcceptanceImageCatalog.Required), Hashes(
                TradingCoreAcceptanceAssemblyCatalog.Required), results,
            now, now.AddMinutes(1), true, []);
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

    [Fact]
    public void CoordinatorPlanRequiresASeparateRollbackImportJob()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var transitionId = Guid.NewGuid().ToString();
        var candidate = new TradingCoreTransitionPlanV1(
            1, "", transitionId, AuthorityTransitionDirections.Rollback,
            TradingAuthorityMode.Remote, TradingAuthorityMode.Shadow, 4, 2,
            now, now.AddMinutes(30), new Uri("https://edge:3543"),
            new Uri("https://core:9443"), "/state/financial-transfer.json", null,
            new FinancialTransferCompatibility(2, "edge", "core", "engine", "artifact",
                "patterns", "calendar", "market-data"), "BrokerTotalEquity",
            new TradingCoreDeploymentTarget("stocktrader", "stocktrader-api",
                "stocktrader-trading-core", "api", "trading-core",
                "edge-image@sha256:" + new string('a', 64),
                "core-shadow-image@sha256:" + new string('b', 64),
                "sha256:" + new string('a', 64),
                "sha256:" + new string('b', 64), "stocktrader-alpaca"),
            new TradingCoreRollbackTarget(
                $"stocktrader-edge-rollback-import-{transitionId}",
                "/state/rollback-import-receipt.json"),
            "isolated-manifest", "shadow-manifest");
        var plan = candidate with { PlanHash = TradingCoreCoordinatorIdentity.Plan(candidate) };

        TradingCoreCoordinatorPolicy.Error(plan).Should().BeNull();
        plan.Rollback!.ImportJobName.Should().StartWith("stocktrader-edge-rollback-import-");
    }

    [Fact]
    public void FinancialExecutionClientIdentityIsStableAndNamespaced()
    {
        var first = FinancialExecutionIdentityPolicy.ClientOrderId("command-42");
        var second = FinancialExecutionIdentityPolicy.ClientOrderId("command-42");

        first.Should().Be(second).And.StartWith("st-").And.HaveLength(35);
        FinancialExecutionIdentityPolicy.ClientOrderId("command-43").Should().NotBe(first);
    }

    private static IReadOnlyDictionary<string, string> Hashes(IEnumerable<string> keys) =>
        keys.ToDictionary(key => key, _ => new string('a', 64));
}

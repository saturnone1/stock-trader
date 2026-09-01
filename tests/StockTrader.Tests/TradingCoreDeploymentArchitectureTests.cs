using FluentAssertions;

namespace StockTrader.Tests;

public sealed class TradingCoreDeploymentArchitectureTests
{
    [Fact]
    public void Broker_egress_requires_the_active_owner_label()
    {
        var root = RepositoryRoot();
        var apiDeployment = Read(root, "k8s", "deployment-api.yaml");
        var coreDeployment = Read(root, "k8s", "deployment-trading-core.yaml");
        var apiPolicy = Read(root, "k8s", "network-policy-api.yaml");
        var corePolicy = Read(root, "k8s", "network-policy-trading-core.yaml");
        var coordinator = Read(root, "workers", "trading-core-cutover-coordinator", "Coordinator.fs");

        apiDeployment.Should().Contain("stocktrader.io/broker-egress: \"__API_BROKER_EGRESS_LABEL__\"");
        coreDeployment.Should().Contain("stocktrader.io/broker-egress: \"__TRADING_CORE_BROKER_EGRESS_LABEL__\"");
        apiPolicy.Should().Contain("stocktrader-api-local-provider-egress");
        apiPolicy.Should().Contain("stocktrader.io/broker-egress: enabled");
        corePolicy.Should().Contain("stocktrader-trading-core-broker-egress");
        corePolicy.Should().Contain("stocktrader.io/broker-egress: enabled");
        coordinator.Should().Contain("labels[\"stocktrader.io/broker-egress\"]");
        coordinator.Should().Contain("then \"disabled\" else \"enabled\"");
        coordinator.Should().Contain("then \"enabled\" else \"disabled\"");
    }

    [Fact]
    public void Remote_edge_image_and_deployment_remove_the_broker_capability_tuple()
    {
        var root = RepositoryRoot();
        var dockerfile = Read(root, "Dockerfile.api");
        var deploy = Read(root, "scripts", "deploy-k3s.sh");

        dockerfile.Should().Contain("FROM runtime-base AS remote");
        dockerfile.Should().Contain("rm -f /app/StockTrader.TradingCore.AlpacaAdapter.dll");
        deploy.Should().Contain("api_network_policy_hash=\"$(sha256sum k8s/network-policy-api.yaml");
        deploy.Should().Contain("/^        - name: ALPACA__/");
        deploy.Should().Contain("__API_BROKER_EGRESS_LABEL__");
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}

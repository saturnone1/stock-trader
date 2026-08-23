using FluentAssertions;

namespace StockTrader.Tests;

public sealed class MarketDataServiceArchitectureTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Service_is_an_independent_fsharp_deployment_without_application_database_access()
    {
        var project = File.ReadAllText(Path.Combine(Root,
            "workers", "market-data-service", "StockTrader.MarketDataService.fsproj"));
        var sources = string.Join('\n', Directory.GetFiles(Path.Combine(Root,
            "workers", "market-data-service"), "*.fs").Select(File.ReadAllText));
        var manifest = File.ReadAllText(Path.Combine(Root,
            "k8s", "deployment-market-data.yaml"));

        project.Should().NotContain("StockTrader.csproj");
        sources.Should().NotContain("AppDbContext");
        sources.Should().NotContain("OhlcvRepository");
        sources.Should().NotContain("BrokerOrder");
        manifest.Should().Contain("serviceAccountName: stocktrader-market-data");
        manifest.Should().Contain("automountServiceAccountToken: false");
        manifest.Should().Contain("__MARKET_DATA_DATA_DIR__");
        manifest.Should().NotContain("__STOCKTRADER_DATA_DIR__");
    }

    [Fact]
    public void Remote_mode_routes_provider_and_bar_authority_through_contract_client()
    {
        var adapters = File.ReadAllText(Path.Combine(Root,
            "Services", "DataFeed", "RemoteMarketDataAdapters.cs"));
        var registrations = File.ReadAllText(Path.Combine(Root,
            "Extensions", "DataServiceExtensions.cs"));
        var streaming = File.ReadAllText(Path.Combine(Root,
            "BackgroundServices", "AlpacaStreamingService.cs"));

        adapters.Should().Contain("MarketDataTransportMode.Remote");
        adapters.Should().Contain("MarketDataServiceClient");
        registrations.Should().Contain("IOhlcvRepository, MarketDataRepositoryRouter");
        registrations.Should().Contain("MarketDataFeedRouter");
        streaming.Should().Contain("In-process Alpaca streaming disabled");
    }

    [Fact]
    public void Deployment_path_owns_backup_tls_rotation_and_rollback_projection()
    {
        var deploy = File.ReadAllText(Path.Combine(Root, "scripts", "deploy-k3s.sh"));

        deploy.Should().Contain("Dockerfile.market-data");
        deploy.Should().Contain("marketdata-pre-");
        deploy.Should().Contain("--project-market-data-rollback");
        File.Exists(Path.Combine(Root, "scripts", "rotate-market-data-tls.sh")).Should().BeTrue();
        File.Exists(Path.Combine(Root, "scripts", "restore-market-data-backup.sh")).Should().BeTrue();
    }

    [Fact]
    public void Api_liveness_does_not_depend_on_the_market_data_service()
    {
        var health = File.ReadAllText(Path.Combine(Root, "Api", "HealthEndpoints.cs"));
        var manifest = File.ReadAllText(Path.Combine(Root, "k8s", "deployment-api.yaml"));

        health.Should().Contain("/health/live");
        manifest.Should().Contain("path: /api/health/live");
        manifest.Should().Contain("readinessProbe:");
        manifest.Should().Contain("path: /api/health");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "StockTrader.csproj")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

using FluentAssertions;

namespace StockTrader.Tests;

public sealed class MlTrainingArchitectureTests
{
    [Fact]
    public void Ml_training_is_an_independent_service_owned_boundary()
    {
        var root = FindRoot();
        var project = Read(root, "workers/ml-training-service/StockTrader.MlTrainingService.fsproj");
        var store = Read(root, "workers/ml-training-service/JobStore.fs");
        var configuration = Read(root, "workers/ml-training-service/Configuration.fs");
        var host = Read(root, "workers/ml-training-service/HttpHost.fs");
        var compute = Read(root, "src/StockTrader.MlTrainingCompute/MlTrainingComputeFacade.cs");
        var deployment = Read(root, "k8s/deployment-ml-training.yaml");
        var apiDeployment = Read(root, "k8s/deployment-api.yaml");
        var adr = Read(root, "docs/architecture/adr/0079-extract-ml-training-service.md");

        project.Should().Contain("StockTrader.MlTrainingCompute");
        project.Should().Contain("Microsoft.Data.Sqlite");
        configuration.Should().Contain("jobs.db");
        store.Should().Contain("publication_revision");
        store.Should().NotContain("stocktrader.db");
        host.Should().Contain("RequireCertificate");
        host.Should().Contain("X-StockTrader-Worker-Secret");
        compute.Should().NotContain("HttpClient");
        compute.Should().NotContain("EntityFrameworkCore");
        compute.Should().NotContain("DateTime.UtcNow");
        deployment.Should().Contain("replicas: 1");
        deployment.Should().Contain("containerPort: 8080");
        deployment.Should().Contain("containerPort: 8443");
        deployment.Should().NotContain("stocktrader.db");
        deployment.Should().NotContain("ml_models");
        apiDeployment.Should().Contain("MlTrainingTransport__Mode");
        adr.Should().Contain("immutable artifact");
    }

    private static string Read(string root, string relative) =>
        File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StockTrader.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

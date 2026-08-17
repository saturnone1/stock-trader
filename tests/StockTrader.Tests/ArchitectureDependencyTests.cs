using FluentAssertions;

namespace StockTrader.Tests;

public class ArchitectureDependencyTests
{
    private static readonly string[] ForbiddenInnerLayerImports =
    [
        "using StockTrader.Services",
        "using StockTrader.Api",
        "using StockTrader.Data",
        "using StockTrader.BackgroundServices"
    ];

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    public void InnerLayersDoNotImportInfrastructureOrDeliveryNamespaces(string layer)
    {
        var repository = FindRepositoryRoot();
        var violations = Directory.GetFiles(Path.Combine(repository, layer), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => ForbiddenInnerLayerImports
                .Where(import => File.ReadAllText(file).Contains(import, StringComparison.Ordinal))
                .Select(import => $"{Path.GetRelativePath(repository, file)} -> {import}"))
            .ToArray();

        violations.Should().BeEmpty(
            $"{layer} 계층은 Services/API/Data/BackgroundServices에 의존하면 안 됩니다");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "StockTrader.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("StockTrader.csproj가 있는 저장소 루트를 찾지 못했습니다.");
    }
}

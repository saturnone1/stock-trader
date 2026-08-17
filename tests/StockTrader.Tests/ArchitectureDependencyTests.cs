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

    [Fact]
    public void LiveStrategyExecutionPathsUseCompiledRepositoryBoundary()
    {
        var repository = FindRepositoryRoot();
        var livePaths = new[]
        {
            "Services/Patterns/PatternDetectionService.cs",
            "Services/Signal/SignalService.cs",
            "BackgroundServices/PositionExitManagerService.cs"
        };
        var forbidden = new[] { "JsonSerializer", "StrategyCompiler.Compile", ".CustomPatterns" };

        var violations = livePaths.SelectMany(path =>
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            return forbidden.Where(source.Contains).Select(token => $"{path} -> {token}");
        }).ToArray();

        violations.Should().BeEmpty(
            "실시간 탐지·추천·청산은 저장 JSON이나 EF 엔티티를 직접 해석하지 않고 ICompiledStrategyRepository를 사용해야 합니다");
    }

    [Fact]
    public void ProgramDelegatesSchemaChangesToVersionedMigrationRunner()
    {
        var repository = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repository, "Program.cs"));
        var initialization = File.ReadAllText(Path.Combine(
            repository, "Extensions/ApplicationInitializationExtensions.cs"));

        program.Should().Contain("InitializeStockTraderAsync(");
        initialization.Should().Contain("DatabaseMigrationRunner");
        (program + initialization).Should().NotContain("ALTER TABLE");
        (program + initialization).Should().NotContain("PRAGMA table_info");
        (program + initialization).Should().NotContain("CREATE TABLE");
        (program + initialization).Should().NotContain("EnsureCreatedAsync");
    }

    [Fact]
    public void BacktestServiceDelegatesOptimizationShapeAndVariantLogic()
    {
        var repository = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repository, "Services/Backtest/BacktestService.cs"));

        source.Should().NotContain("using StockTrader.Api");
        source.Should().NotContain("private static CustomPatternDefinition ClonePatternDefinition(");
        source.Should().NotContain("private static void ApplyOptimizeOverrides(");
        source.Should().NotContain("private static List<OptimizeParamSnapshot> GenerateOptimizeCombinations(");
        source.Should().Contain("StrategyVariantFactory.ClonePatternDefinition(");
        source.Should().Contain("StrategyOptimizationSpace.GenerateOptimizeCombinations(");
    }

    [Fact]
    public void BacktestServiceDelegatesPreparedDataSimulationAndExecutionCosts()
    {
        var repository = FindRepositoryRoot();
        var servicePath = Path.Combine(repository, "Services/Backtest/BacktestService.cs");
        var enginePath = Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs");
        var service = File.ReadAllText(servicePath);
        var engine = File.ReadAllText(enginePath);

        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(800);
        service.Should().Contain("_simulationEngine.RunAsync(");
        service.Should().NotContain("private async Task<BacktestResult> RunSimulationAsync(");
        service.Should().NotContain("volatilityFactor");
        engine.Should().Contain("BacktestExecutionCostLedger");
    }

    [Fact]
    public void BacktestAndLiveRecommendationSharePositionSizingPolicy()
    {
        var repository = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var signalService = File.ReadAllText(Path.Combine(
            repository, "Services/Signal/SignalService.cs"));
        var riskService = File.ReadAllText(Path.Combine(
            repository, "Services/Risk/MultiAccountRiskService.cs"));
        var performance = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/PerformanceCalculator.cs"));

        engine.Should().Contain("new BacktestPortfolioState(");
        engine.Should().Contain("LongPositionSizingPolicy.Calculate(");
        signalService.Should().Contain("LongPositionSizingPolicy.ResolveRiskFraction(");
        signalService.Should().Contain("LongPositionSizingPolicy.ApplyPositionCapitalCap(");
        signalService.Should().Contain("LongPositionSizingPolicy.CalculateAffordableQuantity(");
        riskService.Should().Contain("LongPositionSizingPolicy.CalculateRiskCapital(");
        performance.Should().Contain("LongPositionSizingPolicy.ComputeKellyFraction(");
        engine.Should().NotContain("rollingAvgWin");
        signalService.Should().NotContain("var kelly =");
    }

    [Fact]
    public void BacktestEngineDelegatesExitScalingAndHasNoLegacySecondSimulationLoop()
    {
        var repository = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var executionAdapter = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestExecutionAdapter.cs"));
        var preview = File.ReadAllText(Path.Combine(
            repository, "Api/PatternPreviewEndpoints.cs"));

        engine.Should().Contain("positionExitProcessor.Process(");
        engine.Should().Contain("pendingEntryProcessor.Process(");
        engine.Should().Contain("BacktestOpenPositionFactory.CreateCurrentClose(");
        File.ReadAllLines(Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs"))
            .Length.Should().BeLessThanOrEqualTo(550);
        File.ReadAllLines(Path.Combine(repository, "Services/Backtest/BacktestExecutionAdapter.cs"))
            .Length.Should().BeLessThanOrEqualTo(400);
        engine.Should().NotContain("positionDetector.CheckScaling(");
        engine.Should().NotContain("new BacktestExecutionAdapter.OpenPosition");
        engine.Should().NotContain("LongEntryFillPolicy.Reprice(");
        executionAdapter.Should().NotContain("SimulateSymbolAsync(");
        executionAdapter.Should().NotContain("DetectAsync(");
        preview.Should().Contain("StrategyCatalog.ScalingInDirection");
    }

    [Fact]
    public void EntryAllocationAndCorrelationHaveApplicationPolicyOwners()
    {
        var repository = FindRepositoryRoot();
        var enginePath = Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs");
        var engine = File.ReadAllText(enginePath);
        var preview = File.ReadAllText(Path.Combine(repository, "Api/PatternPreviewEndpoints.cs"));
        var signal = File.ReadAllText(Path.Combine(repository, "Services/Signal/SignalService.cs"));

        File.ReadAllLines(enginePath).Length.Should().BeLessThanOrEqualTo(450);
        engine.Should().Contain("PositionAllocationPolicy.Apply(");
        engine.Should().Contain("PortfolioCorrelationPolicy.ExceedsLimit(");
        engine.Should().NotContain("GetWeightScale(");
        engine.Should().NotContain("ComputePearsonCorrelation(");
        preview.Should().Contain("PositionAllocationPolicy.NormalizeScale(");
        signal.Should().Contain("PositionAllocationPolicy.NormalizeScale(");
    }

    [Fact]
    public void BacktestEntryPathsShareOneEligibilityPolicy()
    {
        var repository = FindRepositoryRoot();
        var enginePath = Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs");
        var engine = File.ReadAllText(enginePath);
        var pending = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPendingEntryProcessor.cs"));

        File.ReadAllLines(enginePath).Length.Should().BeLessThanOrEqualTo(410);
        engine.Should().Contain("BacktestEntryEligibilityPolicy.Evaluate(");
        pending.Should().Contain("BacktestEntryEligibilityPolicy.Evaluate(");
        pending.Should().NotContain("private static bool IsBlocked(");
    }

    [Fact]
    public void OptimizationExecutorDelegatesMarketDataPreparation()
    {
        var repository = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repository, "BackgroundServices/OptimizationJobExecutor.cs"));

        source.Should().Contain("BacktestDataPreparer");
        source.Should().NotContain("GetHistoricalBarsAsync");
        source.Should().NotContain("IndicatorService.ExtractCloses");
        source.Should().NotContain("new PatternSettings()");
        source.Should().NotContain("BacktestService.SymbolPreparedData");
    }

    [Fact]
    public void PreviewAndBacktestUseTheSameLongPositionExecutionPolicy()
    {
        var repository = FindRepositoryRoot();
        var preview = File.ReadAllText(Path.Combine(repository, "Api/PatternPreviewEndpoints.cs"));
        var backtest = File.ReadAllText(Path.Combine(repository, "Services/Backtest/BacktestExecutionAdapter.cs"));

        preview.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        backtest.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        preview.Should().Contain("LongEntryFillPolicy.Reprice(");
        preview.Should().NotContain("current.Low <= position.StopPrice");
        preview.Should().NotContain("current.High >= position.TargetPrice");
    }

    [Fact]
    public void LiveExitManagerDelegatesTradingDecisionsToPurePolicy()
    {
        var repository = FindRepositoryRoot();
        var liveManager = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PositionExitManagerService.cs"));

        liveManager.Should().Contain("LiveLongPositionDecisionPolicy.Evaluate(");
        liveManager.Should().NotContain("position.CurrentPrice <= position.StopLossPrice");
        liveManager.Should().NotContain("position.CurrentPrice >= position.TargetPrice");
        liveManager.Should().NotContain("DateTime.UtcNow");
        liveManager.Should().NotContain("TZConvert");
        liveManager.Should().NotContain("7.0 / 5.0");
        liveManager.Should().Contain("exitCoordinator.SubmitAsync(");
        liveManager.Should().NotContain("brokerService.ClosePositionAsync(");
    }

    [Fact]
    public void AutomaticAndManualExitPathsUseTheSameSubmissionCoordinator()
    {
        var repository = FindRepositoryRoot();
        var orders = File.ReadAllText(Path.Combine(repository, "Api/OrderEndpoints.cs"));
        var portfolio = File.ReadAllText(Path.Combine(repository, "Components/Pages/Portfolio.razor"));

        orders.Should().Contain("exits.SubmitAsync(");
        portfolio.Should().Contain("ExitCoordinator.SubmitAsync(");
        orders.Should().NotContain("broker.ClosePositionAsync(");
        portfolio.Should().NotContain("broker.ClosePositionAsync(");
    }

    [Fact]
    public void ProgramIsAThinCompositionRoot()
    {
        var repository = FindRepositoryRoot();
        var lines = File.ReadAllLines(Path.Combine(repository, "Program.cs"));
        var source = string.Join('\n', lines);

        lines.Length.Should().BeLessThanOrEqualTo(200);
        source.Should().Contain("MapStockTraderApi(");
        source.Should().NotContain("app.MapPost(");
        source.Should().NotContain("app.MapGet(");
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

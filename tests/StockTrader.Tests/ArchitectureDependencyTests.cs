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

    [Fact]
    public void DesktopBacktestDelegatesPureResearchCalculations()
    {
        var repository = FindRepositoryRoot();
        var pagePath = Path.Combine(repository, "desktop-app/src/pages/Backtest.svelte");
        var page = File.ReadAllText(pagePath);
        var research = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestResearch.js"));
        var resultSummary = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestResultSummary.svelte"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(1_910);
        page.Should().Contain("from '../features/backtest/backtestResearch'");
        page.Should().Contain("from '../features/backtest/BacktestResultSummary.svelte'");
        page.Should().Contain("<BacktestResultSummary");
        page.Should().NotContain("백테스트 실패:");
        page.Should().NotContain("타이밍 리포트");
        page.Should().NotContain("function getWhipsawStats(");
        page.Should().NotContain("const factorExperimentPresets = [");
        research.Should().Contain("export function getWhipsawStats(");
        research.Should().Contain("export function getEquityCurveVolatility(");
        resultSummary.Should().Contain("백테스트 실패:");
        resultSummary.Should().Contain("타이밍 리포트");
    }

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
        var tradeLedger = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestTradeLedger.cs"));

        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(800);
        service.Should().Contain("_simulationEngine.RunAsync(");
        service.Should().NotContain("private async Task<BacktestResult> RunSimulationAsync(");
        service.Should().NotContain("volatilityFactor");
        engine.Should().Contain("new BacktestTradeLedger(");
        tradeLedger.Should().Contain("new BacktestExecutionCostLedger(");
    }

    [Fact]
    public void BacktestAndLiveRecommendationSharePositionSizingPolicy()
    {
        var repository = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var entryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var signalService = File.ReadAllText(Path.Combine(
            repository, "Services/Signal/SignalService.cs"));
        var riskService = File.ReadAllText(Path.Combine(
            repository, "Services/Risk/MultiAccountRiskService.cs"));
        var performance = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/PerformanceCalculator.cs"));

        engine.Should().Contain("new BacktestPortfolioState(");
        entryProcessor.Should().Contain("LongPositionSizingPolicy.Calculate(");
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
        var entryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var preview = File.ReadAllText(Path.Combine(
            repository, "Api/PatternPreviewEndpoints.cs"));

        engine.Should().Contain("positionExitProcessor.Process(");
        engine.Should().Contain("pendingEntryProcessor.Process(");
        engine.Should().Contain("_signalEntryProcessor.ProcessAsync(");
        entryProcessor.Should().Contain("BacktestOpenPositionFactory.CreateCurrentClose(");
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
        var entryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var preview = File.ReadAllText(Path.Combine(repository, "Api/PatternPreviewEndpoints.cs"));
        var signal = File.ReadAllText(Path.Combine(repository, "Services/Signal/SignalService.cs"));

        File.ReadAllLines(enginePath).Length.Should().BeLessThanOrEqualTo(450);
        engine.Should().Contain("_signalEntryProcessor.ProcessAsync(");
        entryProcessor.Should().Contain("PositionAllocationPolicy.Apply(");
        entryProcessor.Should().Contain("PortfolioCorrelationPolicy.ExceedsLimit(");
        entryProcessor.Should().NotContain("GetWeightScale(");
        entryProcessor.Should().NotContain("ComputePearsonCorrelation(");
        preview.Should().Contain("PositionAllocationPolicy.NormalizeScale(");
        signal.Should().Contain("PositionAllocationPolicy.NormalizeScale(");
    }

    [Fact]
    public void BacktestEntryPathsShareOneEligibilityPolicy()
    {
        var repository = FindRepositoryRoot();
        var enginePath = Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs");
        var engine = File.ReadAllText(enginePath);
        var entryProcessorPath = Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs");
        var entryProcessor = File.ReadAllText(entryProcessorPath);
        var pending = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPendingEntryProcessor.cs"));

        File.ReadAllLines(enginePath).Length.Should().BeLessThanOrEqualTo(410);
        File.ReadAllLines(entryProcessorPath).Length.Should().BeLessThanOrEqualTo(240);
        engine.Should().Contain("_signalEntryProcessor.ProcessAsync(");
        entryProcessor.Should().Contain("BacktestEntryEligibilityPolicy.Evaluate(");
        pending.Should().Contain("BacktestEntryEligibilityPolicy.Evaluate(");
        pending.Should().NotContain("private static bool IsBlocked(");
    }

    [Fact]
    public void BacktestRuntimeStateHasOneRegistryOwner()
    {
        var repository = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var registry = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestStrategyRuntimeRegistry.cs"));
        var tradeLedger = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestTradeLedger.cs"));
        var entry = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var pending = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPendingEntryProcessor.cs"));
        var exit = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPositionExitProcessor.cs"));

        engine.Should().Contain("new BacktestStrategyRuntimeRegistry(");
        engine.Should().Contain("new BacktestTradeLedger(");
        engine.Should().Contain("runtimeRegistry.BeginStep(");
        engine.Should().NotContain("runtime.RealizedEquity +=");
        tradeLedger.Should().Contain("_runtimeRegistry.ApplyRealizedTrade(");
        registry.Should().Contain("BacktestStrategyTransitionPolicy.RegisterClosedTrade(");
        entry.Should().Contain("context.RuntimeRegistry");
        pending.Should().Contain("context.RuntimeRegistry");
        exit.Should().Contain("context.RuntimeRegistry");
    }

    [Fact]
    public void TerminalLiquidationSettlesTradesAndRemovesOpenPositions()
    {
        var repository = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var liquidator = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestTerminalPositionLiquidator.cs"));

        engine.Should().Contain("BacktestTerminalPositionLiquidator.Liquidate(");
        liquidator.Should().Contain("portfolio.OpenPositions.Remove(symbol)");
        liquidator.Should().Contain("ledger.SettleSince(firstNewTrade)");
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
    public void BarAndLiveExecutionShareCloseDecisionPriority()
    {
        var repository = FindRepositoryRoot();
        var barPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LongPositionExecutionPolicy.cs"));
        var livePolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveLongPositionDecisionPolicy.cs"));

        barPolicy.Should().Contain("LongPositionCloseDecisionPolicy.Resolve(");
        livePolicy.Should().Contain("LongPositionCloseDecisionPolicy.Resolve(");
        barPolicy.Should().NotContain("bar.High >= next.TargetPrice");
        livePolicy.Should().NotContain("currentPrice >= next.TargetPrice");
    }

    [Fact]
    public void BacktestAndLiveShareCumulativeRsi2ExitDecision()
    {
        var repository = FindRepositoryRoot();
        var backtest = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestExecutionAdapter.cs"));
        var live = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PositionExitManagerService.cs"));

        backtest.Should().Contain("CumulativeRsi2ExitDecisionPolicy.Resolve(");
        live.Should().Contain("CumulativeRsi2ExitDecisionPolicy.Resolve(");
        backtest.Should().NotContain("currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold");
        live.Should().NotContain("currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold");
    }

    [Fact]
    public void TqqqEntryBacktestAndLiveShareTheTrendStopPolicy()
    {
        var repository = FindRepositoryRoot();
        var detector = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/Tqqq200SmaDetector.cs"));
        var preparer = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestDataPreparer.cs"));
        var live = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PositionExitManagerService.cs"));

        detector.Should().Contain("Tqqq200SmaExecutionPolicy.ResolveEntryLevels(");
        preparer.Should().Contain("Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(");
        live.Should().Contain("Tqqq200SmaExecutionPolicy.ResolveProtectiveStopFloor(");
        detector.Should().NotContain("smaValue * 0.99m");
        detector.Should().NotContain("smaValue * 1.50m");
    }

    [Fact]
    public void PreviewBacktestAndLiveShareTheExitPolicyCatalog()
    {
        var repository = FindRepositoryRoot();
        var preview = File.ReadAllText(Path.Combine(repository, "Api/PatternPreviewEndpoints.cs"));
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var entryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestExecutionAdapter.cs"));
        var live = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PositionExitManagerService.cs"));

        preview.Should().Contain("LongPositionExitPolicyCatalog.ForCustom(");
        engine.Should().Contain("_signalEntryProcessor.ProcessAsync(");
        entryProcessor.Should().Contain("LongPositionExitPolicyCatalog.ForCustom(");
        adapter.Should().Contain("LongPositionExitPolicyCatalog.ForPattern(");
        live.Should().Contain("LongPositionExitPolicyCatalog.ForPattern(");
        live.Should().Contain("LongPositionExitPolicyCatalog.ForCustom(");
        adapter.Should().NotContain("record PatternExitProfile(");
        live.Should().NotContain("BacktestExecutionAdapter.PatternExitProfile");
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
        liveManager.Should().Contain("exitCoordinator.ReconcileAsync(");
        liveManager.Should().NotContain("brokerService.ClosePositionAsync(");
        liveManager.Should().NotContain("ExitOrderReconciliationPolicy.Resolve(");
        liveManager.Should().NotContain("ReleasePositionExitClaimAsync(");
        liveManager.Should().NotContain("TryCompletePositionExitAsync(");
    }

    [Fact]
    public void PositionApisShareOperationalExitStatusContract()
    {
        var repository = FindRepositoryRoot();
        var endpointPaths = new[]
        {
            "Api/TradeEndpoints.cs",
            "Api/PortfolioEndpoints.cs",
            "Api/DashboardEndpoints.cs"
        };

        foreach (var path in endpointPaths)
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            source.Should().Contain("OpenPositionResponseMapper.Map(");
            source.Should().NotContain("HoldingDays    = (DateTime.UtcNow");
        }

        var orders = File.ReadAllText(Path.Combine(repository, "Api/OrderEndpoints.cs"));
        orders.Should().Contain("/reconcile-position-exit");
        orders.Should().Contain("exits.ReconcileAsync(");
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

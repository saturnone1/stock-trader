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
        var performanceBreakdown = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestPerformanceBreakdown.svelte"));
        var validationResults = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestValidationResults.svelte"));
        var tradeHistory = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestTradeHistory.svelte"));
        var factorRanking = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestFactorRanking.svelte"));
        var factorLabPanel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestFactorLabPanel.svelte"));
        var factorEditor = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestFactorExperimentEditor.svelte"));
        var factorCandidates = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestFactorCandidates.svelte"));
        var timingOptions = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestTimingOptions.svelte"));
        var scenarioComparison = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestScenarioComparison.svelte"));
        var universeControls = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestUniverseControls.svelte"));
        var universeResults = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestUniverseComparison.svelte"));
        var executionInputs = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestExecutionInputs.svelte"));
        var riskSettings = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestRiskSettings.svelte"));
        var patternSelection = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/BacktestPatternSelection.svelte"));
        var scenarioPlanning = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestScenarioPlanning.js"));
        var scenarioPlanningTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestScenarioPlanning.test.js"));
        var resultAnalysis = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestResultAnalysis.js"));
        var resultAnalysisTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestResultAnalysis.test.js"));
        var execution = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestExecution.js"));
        var executionTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestExecution.test.js"));
        var researchTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestResearch.test.js"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(720);
        page.Should().Contain("from '../features/backtest/backtestResearch'");
        page.Should().Contain("from '../features/backtest/BacktestResultSummary.svelte'");
        page.Should().Contain("<BacktestResultSummary");
        page.Should().Contain("<BacktestPerformanceBreakdown");
        page.Should().Contain("<BacktestValidationResults");
        page.Should().Contain("<BacktestTradeHistory");
        page.Should().Contain("<BacktestFactorRanking");
        page.Should().Contain("<BacktestFactorLabPanel");
        page.Should().Contain("<BacktestTimingOptions");
        page.Should().Contain("<BacktestScenarioComparison");
        page.Should().Contain("<BacktestUniverseControls");
        page.Should().Contain("<BacktestUniverseComparison");
        page.Should().Contain("<BacktestExecutionInputs");
        page.Should().Contain("<BacktestRiskSettings");
        page.Should().Contain("<BacktestPatternSelection");
        page.Should().NotContain("백테스트 실패:");
        page.Should().NotContain("타이밍 리포트");
        page.Should().NotContain("종목별 성과");
        page.Should().NotContain(">워크포워드 결과</div>");
        page.Should().NotContain(">최근 거래</div>");
        page.Should().NotContain(">팩터 실험실 랭킹</div>");
        page.Should().NotContain(">커스텀 팩터 조합</div>");
        page.Should().NotContain(">실행 가능한 프리셋</div>");
        page.Should().NotContain(">비교 기간 조합</div>");
        page.Should().NotContain(">타이밍·팩터 비교 결과</div>");
        page.Should().NotContain(">유니버스·팩터 비교</div>");
        page.Should().NotContain(">필터 전/후 기준 비교</div>");
        page.Should().NotContain(">거래당 리스크</div>");
        page.Should().NotContain(">포트폴리오 비중 전략</div>");
        page.Should().NotContain(">패턴 선택</div>");
        page.Should().NotContain("function getWhipsawStats(");
        page.Should().NotContain("const factorExperimentPresets = [");
        page.Should().NotContain("function safeParseJson(");
        page.Should().NotContain("function buildTimingRule(");
        page.Should().NotContain("totalReturn * 140");
        page.Should().NotContain("getWhipsawStats(");
        page.Should().NotContain("getEquityCurveVolatility(");
        page.Should().NotContain("function buildRequestPayload(");
        page.Should().NotContain("function runSingleBacktestRequest(");
        research.Should().Contain("export function getWhipsawStats(");
        research.Should().Contain("export function getEquityCurveVolatility(");
        research.Should().Contain("export function factorReturnLift(");
        research.Should().Contain("export function factorDrawdownImprovement(");
        resultSummary.Should().Contain("백테스트 실패:");
        resultSummary.Should().Contain("타이밍 리포트");
        performanceBreakdown.Should().Contain("종목별 성과");
        performanceBreakdown.Should().Contain("레짐별 성과");
        validationResults.Should().Contain("워크포워드 결과");
        validationResults.Should().Contain("몬테카를로 결과");
        tradeHistory.Should().Contain("최근 거래");
        factorRanking.Should().Contain("팩터 실험실 랭킹");
        factorLabPanel.Should().Contain("<BacktestFactorExperimentEditor");
        factorLabPanel.Should().Contain("<BacktestFactorCandidates");
        factorEditor.Should().Contain("커스텀 팩터 조합");
        factorCandidates.Should().Contain("실행 여부");
        timingOptions.Should().Contain("비교 기간 조합");
        scenarioComparison.Should().Contain("타이밍·팩터 비교 결과");
        scenarioComparison.Should().Contain("onSelect(row.key)");
        universeControls.Should().Contain("bind:checked={universeComparison.includeCombined}");
        universeControls.Should().Contain("교집합 필터 후");
        universeResults.Should().Contain("필터 전/후 기준 비교");
        executionInputs.Should().Contain("warning");
        executionInputs.Should().Contain("bind:value={form.timeFrame}");
        riskSettings.Should().Contain("bind:value={form.riskPerTradePercent}");
        riskSettings.Should().Contain("bind:checked={form.useWeightStrategy}");
        patternSelection.Should().Contain("onRun");
        patternSelection.Should().Contain("패턴 선택");
        scenarioPlanning.Should().Contain("export function buildScenarioPatterns(");
        scenarioPlanning.Should().Contain("export function buildUniverseVariants(");
        scenarioPlanning.Should().Contain("[...symbols].sort().join('|')");
        scenarioPlanningTests.Should().Contain("source[0].raw.exitRulesJson");
        scenarioPlanningTests.Should().Contain("deduplicate identical symbol sets");
        resultAnalysis.Should().Contain("export function calculateComparisonDelta(");
        resultAnalysis.Should().Contain("export function buildFactorLabRankingRows(");
        resultAnalysisTests.Should().Contain("matching group baseline");
        execution.Should().Contain("export function buildBacktestRequestPayload(");
        execution.Should().Contain("export async function runBacktestScenarios(");
        execution.Should().Contain("export async function runPlainBacktest(");
        executionTests.Should().Contain("executes sequentially");
        executionTests.Should().Contain("portfolio weight strategy only when enabled");
        researchTests.Should().Contain("API returnPct contract");
    }

    [Fact]
    public void PatternBuilderDelegatesStrategySafetyValidation()
    {
        var repository = FindRepositoryRoot();
        var pagePath = Path.Combine(repository, "desktop-app/src/pages/PatternBuilder.svelte");
        var page = File.ReadAllText(pagePath);
        var validation = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternValidation.js"));
        var validationTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternValidation.test.js"));
        var workspace = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternWorkspace.js"));
        var workspaceTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternWorkspace.test.js"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(1260);
        page.Should().Contain("from '../features/pattern-builder/patternValidation'");
        page.Should().Contain("from '../features/pattern-builder/patternWorkspace'");
        page.Should().NotContain("function collectValidationIssues(");
        page.Should().NotContain("function buildWorkspace(");
        page.Should().NotContain("function buildPatternPayload(");
        page.Should().NotContain("function normalizeRule(");
        page.Should().Contain("collectPatternValidationIssues(workspace");
        page.Should().Contain("workspaceModel.configure({ indicatorFieldConfigs, dynamicExitFieldConfigs })");
        validation.Should().Contain("export function collectPatternValidationIssues(");
        validation.Should().Contain("supportsPartialExit");
        validation.Should().Contain("supportsScaling");
        validationTests.Should().Contain("invalid MACD ordering");
        validationTests.Should().Contain("cannot silently contain empty conditions");
        validationTests.Should().Contain("not supported by the execution engine");
        workspace.Should().Contain("export function createPatternWorkspaceModel(");
        workspace.Should().Contain("entryGroupsJson: JSON.stringify(entryGroups)");
        workspace.Should().Contain("exitGroupsJson: JSON.stringify(exitGroups)");
        workspaceTests.Should().Contain("legacy flat rules are promoted");
        workspaceTests.Should().Contain("malformed optional JSON");
        workspaceTests.Should().Contain("round trip preserves grouped execution semantics");
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

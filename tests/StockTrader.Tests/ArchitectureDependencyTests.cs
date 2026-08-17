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
    public void DesktopOptimizationDelegatesFormResultsAndPureCalculations()
    {
        var repository = FindRepositoryRoot();
        var pagePath = Path.Combine(repository, "desktop-app/src/pages/Optimization.svelte");
        var page = File.ReadAllText(pagePath);
        var model = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/optimizationModel.js"));
        var modelTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/optimizationModel.test.js"));
        var form = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/OptimizationJobForm.svelte"));
        var jobs = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/OptimizationJobList.svelte"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(400);
        page.Should().Contain("<OptimizationJobForm");
        page.Should().Contain("<OptimizationJobList");
        page.Should().Contain("buildOptimizationJob(form, pattern)");
        page.Should().NotContain("function parseNumberList(");
        page.Should().NotContain("function getResultInsights(");
        page.Should().NotContain("formatSignedPercent(");
        form.Should().Contain("estimatedCombinationCount(form)");
        jobs.Should().Contain("resultInsights(result, results)");
        model.Should().Contain("export function buildOptimizationJob(");
        model.Should().Contain("export function formatSignedPercent(");
        modelTests.Should().Contain("without a runtime error");
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
        var editorCommands = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternEditorCommands.js"));
        var editorCommandTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternEditorCommands.test.js"));
        var workspaceSidebar = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternWorkspaceSidebar.svelte"));
        var strategyTree = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternStrategyTree.svelte"));
        var ruleInspector = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternRuleInspector.svelte"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(630);
        page.Should().Contain("from '../features/pattern-builder/patternValidation'");
        page.Should().Contain("from '../features/pattern-builder/patternWorkspace'");
        page.Should().NotContain("function collectValidationIssues(");
        page.Should().NotContain("function buildWorkspace(");
        page.Should().NotContain("function buildPatternPayload(");
        page.Should().NotContain("function normalizeRule(");
        page.Should().NotContain(".splice(");
        page.Should().NotContain("JSON.parse(JSON.stringify");
        page.Should().NotContain("언제 살까?");
        page.Should().NotContain("선택한 조건 바꾸기");
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
        editorCommands.Should().Contain("export function createPatternEditorCommands(");
        editorCommands.Should().Contain("return { workspace, selectedNode, changed: false }");
        editorCommandTests.Should().Contain("creates exactly the requested buy or sell condition");
        editorCommandTests.Should().Contain("do not dirty state at list boundaries");
        editorCommandTests.Should().Contain("safe no-ops");
        page.Should().Contain("<PatternWorkspaceSidebar");
        page.Should().Contain("<PatternStrategyTree");
        page.Should().Contain("<PatternRuleInspector");
        workspaceSidebar.Should().Contain("내 매매 전략");
        workspaceSidebar.Should().Contain("bind:value={newPatternName}");
        strategyTree.Should().Contain("언제 살까?");
        strategyTree.Should().Contain("언제 팔까?");
        strategyTree.Should().Contain("추가 매수·분할 매도");
        ruleInspector.Should().Contain("선택한 조건 바꾸기");
        ruleInspector.Should().Contain("bind:value={workspace.timeFrame}");
        ruleInspector.Should().Contain("실시간 감시와 자동 주문에 연결");
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
    public void RuleBasedDetectorDelegatesIndicatorMathAndEvaluationCache()
    {
        var repository = FindRepositoryRoot();
        var detectorPath = Path.Combine(repository, "Services/Patterns/RuleBasedDetector.cs");
        var evaluatorPath = Path.Combine(repository, "Services/Patterns/RuleIndicatorEvaluator.cs");
        var conditionPath = Path.Combine(repository, "Services/Patterns/RuleConditionEvaluator.cs");
        var detector = File.ReadAllText(detectorPath);
        var evaluator = File.ReadAllText(evaluatorPath);
        var conditions = File.ReadAllText(conditionPath);
        var evaluatorTests = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/RuleIndicatorEvaluatorTests.cs"));

        File.ReadAllLines(detectorPath).Length.Should().BeLessThanOrEqualTo(500);
        File.ReadAllLines(evaluatorPath).Length.Should().BeLessThanOrEqualTo(660);
        detector.Should().Contain("_indicatorEvaluator.CreateContext(");
        detector.Should().Contain("new RuleConditionEvaluator(_indicatorEvaluator)");
        conditions.Should().Contain("_indicators.Compute(");
        detector.Should().NotContain("switch (indicator.ToUpperInvariant())");
        detector.Should().NotContain("case \"RSI\"");
        detector.Should().NotContain("ComputeAdx(");
        evaluator.Should().Contain("switch (indicator.ToUpperInvariant())");
        evaluator.Should().Contain("case \"RSI\"");
        evaluator.Should().Contain("case \"VOLATILITY_20D\"");
        evaluator.Should().Contain("private readonly Dictionary<string, object> _cache");
        evaluatorTests.Should().Contain("CachesIndicatorWithinEvaluationContext");
        evaluatorTests.Should().Contain("DoesNotLeakCachedValuesAcrossSymbols");
        evaluatorTests.Should().Contain("PreservesCurrentAndPreviousBarOffsetContract");
    }

    [Fact]
    public void RuleBasedDetectorDelegatesConditionComparisonAndGroupAggregation()
    {
        var repository = FindRepositoryRoot();
        var detectorPath = Path.Combine(repository, "Services/Patterns/RuleBasedDetector.cs");
        var conditionPath = Path.Combine(repository, "Services/Patterns/RuleConditionEvaluator.cs");
        var groupPath = Path.Combine(repository, "Services/Patterns/RuleGroupEvaluator.cs");
        var detector = File.ReadAllText(detectorPath);
        var conditions = File.ReadAllText(conditionPath);
        var groups = File.ReadAllText(groupPath);
        var tests = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/RuleConditionEvaluatorTests.cs"));

        File.ReadAllLines(detectorPath).Length.Should().BeLessThanOrEqualTo(340);
        detector.Should().Contain("_conditionEvaluator.Evaluate(");
        detector.Should().Contain("_groupEvaluator.Evaluate(");
        detector.Should().NotContain("crosses_above\" =>");
        detector.Should().NotContain("private (bool passed, string desc) EvaluateRule");
        detector.Should().NotContain("private (bool passed, decimal matchedWeight");
        conditions.Should().Contain("crosses_above\" =>");
        conditions.Should().Contain("IndicatorCatalog.RequiredBars(");
        conditions.Should().Contain("bar.Timestamp <= referenceAsOf.Value");
        groups.Should().Contain("matchedWeight +=");
        groups.Should().Contain("groupMatches.Count > 0");
        tests.Should().Contain("Compare_PreservesOperatorBoundarySemantics");
        tests.Should().Contain("ReferenceSymbolCannotReadPastTheExplicitAsOfBoundary");
        tests.Should().Contain("GroupsOwnsNestedLogicWeightAndExplanationAggregation");
    }

    [Fact]
    public void RuleBasedDetectorDelegatesDynamicExitPriceSelection()
    {
        var repository = FindRepositoryRoot();
        var detectorPath = Path.Combine(repository, "Services/Patterns/RuleBasedDetector.cs");
        var policyPath = Path.Combine(repository, "Services/Patterns/DynamicExitPricePolicy.cs");
        var detector = File.ReadAllText(detectorPath);
        var policy = File.ReadAllText(policyPath);
        var tests = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/DynamicExitPricePolicyTests.cs"));

        File.ReadAllLines(detectorPath).Length.Should().BeLessThanOrEqualTo(280);
        detector.Should().Contain("DynamicExitPricePolicy.Resolve(");
        detector.Should().Contain("_timeProvider.GetUtcNow().UtcDateTime");
        detector.Should().NotContain("DateTime.UtcNow");
        detector.Should().NotContain("\"BOLLINGER_LOWER\" =>");
        detector.Should().NotContain("GetPrevLow(");
        policy.Should().Contain("\"BOLLINGER_LOWER\" =>");
        policy.Should().Contain("\"R_MULTIPLE\" =>");
        policy.Should().Contain("current.Close - stop");
        tests.Should().Contain("UsesStrategyAtrDefaultsWhenNoDynamicConfigurationExists");
        tests.Should().Contain("PreviousRangeExcludesTheCurrentBar");
        tests.Should().Contain("IndicatorBasedLevelsUseTheSharedEvaluationContext");
    }

    [Fact]
    public void ProductionPathsUseTheCustomStrategyDetectorContractAndFactory()
    {
        var repository = FindRepositoryRoot();
        var factoryPath = Path.Combine(repository, "Services/Patterns/CustomStrategyDetectorFactory.cs");
        var detectorPath = Path.Combine(repository, "Services/Patterns/RuleBasedDetector.cs");
        var contract = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/ICustomStrategyDetector.cs"));
        var factory = File.ReadAllText(factoryPath);
        var detector = File.ReadAllText(detectorPath);
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/PatternServiceExtensions.cs"));
        var productionRoots = new[] { "Api", "Application", "BackgroundServices", "Services" };
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(factoryPath),
            Path.GetFullPath(detectorPath)
        };
        var forbidden = new[]
        {
            "new RuleBasedDetector(",
            "OfType<RuleBasedDetector>",
            "as RuleBasedDetector"
        };
        var violations = productionRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(repository, root), "*.cs", SearchOption.AllDirectories))
            .Where(path => !excluded.Contains(Path.GetFullPath(path)))
            .SelectMany(path => forbidden
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(repository, path)} -> {token}"))
            .ToArray();

        violations.Should().BeEmpty(
            "미리보기·백테스트·최적화·실시간 경로는 구체 감지기를 직접 조립하거나 캐스팅하면 안 됩니다");
        contract.Should().Contain("public interface ICustomStrategyDetector : IPatternDetector");
        contract.Should().Contain("public interface ICustomStrategyDetectorFactory");
        factory.Should().Contain("new RuleBasedDetector(_indicators, strategy, _timeProvider)");
        detector.Should().Contain("internal RuleBasedDetector(");
        registrations.Should().Contain(
            "AddSingleton<ICustomStrategyDetectorFactory, CustomStrategyDetectorFactory>()");

        var factoryTests = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/CustomStrategyDetectorFactoryTests.cs"));
        factoryTests.Should().Contain("FromCompiledStrategyPreservesTheExactCompiledAggregate");
        factoryTests.Should().Contain("ReturnsAnIsolatedRuntimeForEveryExecutionScope");
        factoryTests.Should().Contain("InvalidDefinitionCannotBypassCentralCompilation");
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
    public void StartupUsesOnlyEfSchemaMigrationsAndRejectsUnbaselinedDatabases()
    {
        var repository = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repository, "Program.cs"));
        var initialization = File.ReadAllText(Path.Combine(
            repository, "Extensions/ApplicationInitializationExtensions.cs"));

        program.Should().Contain("InitializeStockTraderAsync(");
        initialization.Should().Contain("DatabaseSchemaMigrator");
        initialization.Should().NotContain("DatabaseMigrationRunner");
        (program + initialization).Should().NotContain("ALTER TABLE");
        (program + initialization).Should().NotContain("PRAGMA table_info");
        (program + initialization).Should().NotContain("CREATE TABLE");
        (program + initialization).Should().NotContain("EnsureCreatedAsync");

        var schemaMigrator = File.ReadAllText(Path.Combine(
            repository, "Data/Migrations/DatabaseSchemaMigrator.cs"));
        var efMigration = Directory.EnumerateFiles(
                Path.Combine(repository, "Data/EfMigrations"), "*_InitialSchema.cs")
            .Single();
        var toolManifest = File.ReadAllText(Path.Combine(repository, "dotnet-tools.json"));

        schemaMigrator.Should().Contain("_db.Database.MigrateAsync(");
        schemaMigrator.Should().Contain("EF 마이그레이션 이력이 없는 기존 데이터베이스");
        schemaMigrator.Should().NotContain("DatabaseMigrationRunner");
        schemaMigrator.Should().NotContain("ExecuteSqlRaw");
        schemaMigrator.Should().NotContain("ALTER TABLE");
        schemaMigrator.Should().NotContain("CREATE TABLE");
        program.Should().NotContain("--verify-ef-baseline");
        program.Should().Contain("--verify-database-migrations");
        program.Should().Contain("--migrate-database");
        program.Should().Contain("MigrateDatabaseOnlyAsync()");
        program.Should().Contain("Environment.ExitCode = 1;");
        initialization.Should().Contain("DatabaseMigrationStatusProvider");
        var health = File.ReadAllText(Path.Combine(repository, "Api/HealthEndpoints.cs"));
        health.Should().Contain("DatabaseMigrationStatusProvider");
        health.Should().Contain("databaseMigration");
        File.ReadAllText(efMigration).Should().Contain("migrationBuilder.CreateTable(");
        File.Exists(Path.Combine(
            repository, "Data/EfMigrations/AppDbContextModelSnapshot.cs")).Should().BeTrue();
        toolManifest.Should().Contain("\"dotnet-ef\"");
        toolManifest.Should().Contain("\"10.0.10\"");

        Directory.EnumerateFiles(Path.Combine(repository, "Data/Migrations"), "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .Should().Equal("DatabaseMigrationStatusProvider", "DatabaseSchemaMigrator");
        File.Exists(Path.Combine(repository, "docs/architecture/adr/0004-retire-handwritten-migrations.md"))
            .Should().BeTrue();
    }

    [Fact]
    public void CanonicalK3sDeployBacksUpAndMigratesTheDatabaseBeforeRollout()
    {
        var repository = FindRepositoryRoot();
        var deploy = File.ReadAllText(Path.Combine(repository, "scripts/deploy-k3s.sh"));

        deploy.Should().Contain("scale deployment stocktrader-api --replicas=0");
        deploy.Should().Contain("sqlite3 \"$data_dir/stocktrader.db\" \".backup '$backup_path'\"");
        deploy.Should().Contain("PRAGMA quick_check;");
        deploy.Should().Contain("dotnet StockTrader.dll --migrate-database");
        deploy.IndexOf("--migrate-database", StringComparison.Ordinal).Should().BeLessThan(
            deploy.IndexOf("deployment-api.yaml", StringComparison.Ordinal));
    }

    [Fact]
    public void StoredStrategiesHaveAnExplicitFailClosedDocumentVersionBoundary()
    {
        var repository = FindRepositoryRoot();
        var model = File.ReadAllText(Path.Combine(repository, "Models/CustomPatternDefinition.cs"));
        var versions = File.ReadAllText(Path.Combine(
            repository, "Domain/Strategies/StrategyDocumentVersions.cs"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/StrategyDocumentVersionPolicy.cs"));
        var compiler = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/StrategyCompiler.cs"));
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/CustomPatternEndpoints.cs"));
        var migration = Directory.EnumerateFiles(
                Path.Combine(repository, "Data/EfMigrations"), "*_AddStrategyDocumentVersion.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));

        model.Should().Contain("DocumentVersion { get; set; } = StrategyDocumentVersions.Current;");
        versions.Should().Contain("public const int LegacyUnversioned = 0;");
        versions.Should().Contain("public const int Current = 1;");
        policy.Should().Contain("StrategyDocumentVersions.LegacyUnversioned or StrategyDocumentVersions.Current");
        compiler.Should().Contain("StrategyDocumentVersionPolicy.Validate(pattern.DocumentVersion)");
        endpoints.Should().Contain("StrategyDocumentVersionPolicy.StampCurrent(input)");
        endpoints.Should().Contain("StrategyDocumentVersionPolicy.StampCurrent(existing)");
        File.ReadAllText(migration).Should().Contain(
            "defaultValue: StockTrader.Domain.Strategies.StrategyDocumentVersions.Current");
    }

    [Fact]
    public void ApiHostingUsesOneCanonicalContainerListener()
    {
        var repository = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(repository, "appsettings.json"));
        var apiDockerfile = File.ReadAllText(Path.Combine(repository, "Dockerfile.api"));
        var deployment = File.ReadAllText(Path.Combine(repository, "k8s/deployment-api.yaml"));
        var compose = File.ReadAllText(Path.Combine(repository, "docker-compose.yml"));

        settings.Should().NotContain("\"Kestrel\"");
        apiDockerfile.Should().Contain("ASPNETCORE_HTTP_PORTS=5239");
        apiDockerfile.Should().Contain("EXPOSE 5239");
        apiDockerfile.Should().NotContain("ASPNETCORE_URLS");
        deployment.Should().NotContain("ASPNETCORE_URLS");
        deployment.Should().Contain("containerPort: 5239");
        deployment.Should().Contain("targetPort: 5239");
        deployment.Split("timeoutSeconds: 3", StringSplitOptions.None).Length.Should().Be(3,
            "readiness와 liveness 모두 초기 SQLite 상태 조회에 충분한 제한 시간을 가져야 합니다");
        compose.Should().Contain("\"5239:5239\"");
        compose.Should().Contain("dockerfile: Dockerfile.api");
        compose.Should().NotContain("ASPNETCORE_URLS");
    }

    [Fact]
    public void SvelteAndSplitContainersAreTheOnlyUiAndDeploymentPaths()
    {
        var repository = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repository, "Program.cs"));
        var project = File.ReadAllText(Path.Combine(repository, "StockTrader.csproj"));
        var apiDeployment = File.ReadAllText(Path.Combine(repository, "k8s/deployment-api.yaml"));
        var desktopNginx = File.ReadAllText(Path.Combine(repository, "desktop-app/nginx.conf"));

        var legacyUiFiles = new[] { "Components", "wwwroot" }
            .Select(folder => Path.Combine(repository, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
        legacyUiFiles.Should().BeEmpty();
        program.Should().NotContain("AddRazorComponents");
        program.Should().NotContain("MapRazorComponents");
        program.Should().NotContain("MapStaticAssets");
        project.Should().NotContain("MudBlazor");
        project.Should().NotContain("Blazor-ApexCharts");

        Directory.EnumerateFiles(repository, "Dockerfile*")
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo("Dockerfile.api", "Dockerfile.desktop");
        Directory.EnumerateFiles(repository, "docker-compose*.yml")
            .Select(Path.GetFileName)
            .Should().Equal("docker-compose.yml");
        File.Exists(Path.Combine(repository, "scripts/deploy-k3s.sh")).Should().BeTrue();
        File.Exists(Path.Combine(repository, "deploy-k3s.sh")).Should().BeFalse();
        File.Exists(Path.Combine(repository, "k8s/deployment.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(repository, "k8s/service.yaml")).Should().BeFalse();
        File.Exists(Path.Combine(repository, "k8s/ingress.yaml")).Should().BeFalse();
        apiDeployment.Should().Contain("type: Recreate");
        desktopNginx.Should().Contain("location /api/");
        desktopNginx.Should().Contain("proxy_pass http://stocktrader-api:5239");
    }

    [Fact]
    public void BacktestServiceDelegatesOptimizationShapeAndVariantLogic()
    {
        var repository = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(repository, "Services/Backtest/BacktestService.cs"));
        var optimization = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestOptimizationService.cs"));

        service.Should().NotContain("using StockTrader.Api");
        service.Should().Contain("_optimization.RunAsync(");
        service.Should().NotContain("StrategyVariantFactory.ClonePatternDefinition(");
        service.Should().NotContain("StrategyOptimizationSpace.GenerateOptimizeCombinations(");
        optimization.Should().NotContain("private static CustomPatternDefinition ClonePatternDefinition(");
        optimization.Should().NotContain("private static void ApplyOptimizeOverrides(");
        optimization.Should().NotContain("private static List<OptimizeParamSnapshot> GenerateOptimizeCombinations(");
        optimization.Should().Contain("StrategyVariantFactory.ClonePatternDefinition(");
        optimization.Should().Contain("StrategyOptimizationSpace.GenerateOptimizeCombinations(");
        File.ReadAllLines(Path.Combine(
            repository, "Services/Backtest/BacktestOptimizationService.cs"))
            .Length.Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public void BacktestServiceDelegatesPreparedDataSimulationAndExecutionCosts()
    {
        var repository = FindRepositoryRoot();
        var servicePath = Path.Combine(repository, "Services/Backtest/BacktestService.cs");
        var enginePath = Path.Combine(repository, "Services/Backtest/BacktestSimulationEngine.cs");
        var runnerPath = Path.Combine(repository, "Services/Backtest/BacktestPreparedSimulationRunner.cs");
        var service = File.ReadAllText(servicePath);
        var engine = File.ReadAllText(enginePath);
        var runner = File.ReadAllText(runnerPath);
        var tradeLedger = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestTradeLedger.cs"));

        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(500);
        service.Should().Contain("_simulationEngine.RunAsync(");
        service.Should().Contain("_preparedRunner.RunAsync(");
        service.Should().NotContain("private async Task<BacktestResult> RunSimulationAsync(");
        service.Should().NotContain("volatilityFactor");
        runner.Should().Contain("_dataPreparer.Slice(");
        runner.Should().Contain("_simulation.RunAsync(");
        File.ReadAllLines(runnerPath).Length.Should().BeLessThanOrEqualTo(150);
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
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));

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
        var preview = File.ReadAllText(Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));
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
    public void PreviewBacktestAndLiveShareStrategyEntryEligibilityPolicy()
    {
        var repository = FindRepositoryRoot();
        var sharedPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/StrategyEntryEligibilityPolicy.cs"));
        var preview = File.ReadAllText(Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));
        var backtest = File.ReadAllText(Path.Combine(
            repository, "Application/Backtesting/BacktestEntryEligibilityPolicy.cs"));
        var live = File.ReadAllText(Path.Combine(
            repository, "Services/Signal/SignalService.cs"));

        sharedPolicy.Should().Contain("public static class StrategyEntryEligibilityPolicy");
        preview.Should().Contain("StrategyEntryEligibilityPolicy.Evaluate(");
        backtest.Should().Contain("StrategyEntryEligibilityPolicy.Evaluate(");
        live.Should().Contain("StrategyEntryEligibilityPolicy.Evaluate(");
        live.Should().NotContain("DateTime.UtcNow");
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
    public void PreviewBacktestAndLiveUseTheSameLongPositionExecutionPolicy()
    {
        var repository = FindRepositoryRoot();
        var preview = File.ReadAllText(Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));
        var backtest = File.ReadAllText(Path.Combine(repository, "Services/Backtest/BacktestExecutionAdapter.cs"));
        var order = File.ReadAllText(Path.Combine(repository, "Services/Order/OrderService.cs"));
        var parity = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/CustomStrategyExecutionParityTests.cs"));

        preview.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        backtest.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        preview.Should().Contain("LongEntryFillPolicy.Reprice(");
        order.Should().Contain("LongEntryFillPolicy.ReanchorExecutedFill(");
        order.Should().Contain("_timeProvider.GetUtcNow()");
        order.Should().NotContain("DateTime.UtcNow");
        order.Should().NotContain("actualEntry - stopDistance");
        order.Should().NotContain("actualEntry + targetDistance");
        parity.Should().Contain("PreviewBacktestAndLiveFill_RunTheSameCompiledNextOpenStrategy");
        parity.Should().Contain("previewEntry.StopPrice.Should().Be(liveFill.StopPrice)");
        parity.Should().Contain("liveExit.Reason.Should().Be(previewExit.Reason)");
        preview.Should().NotContain("current.Low <= position.StopPrice");
        preview.Should().NotContain("current.High >= position.TargetPrice");
    }

    [Fact]
    public void LiveDailyScannerUsesCentralClockAndRegimePolicy()
    {
        var repository = FindRepositoryRoot();
        var scanner = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PatternScannerService.cs"));

        scanner.Should().Contain("_timeProvider.GetUtcNow()");
        scanner.Should().Contain("StrategyEvaluationPolicy.RegimeTrendBars");
        scanner.Should().Contain("StrategyEvaluationPolicy.RegimeLookbackCalendarDays");
        scanner.Should().Contain("StrategyEvaluationPolicy.LiveDailySignalLookbackDays");
        scanner.Should().NotContain("DateTime.UtcNow");
        scanner.Should().NotContain("AddDays(-400)");
        scanner.Should().NotContain("SMA(closes, 200)");
    }

    [Fact]
    public void PatternPreviewEndpointIsAThinHttpAdapterOverDeterministicSimulation()
    {
        var repository = FindRepositoryRoot();
        var endpointPath = Path.Combine(repository, "Api/PatternPreviewEndpoints.cs");
        var endpoint = File.ReadAllText(endpointPath);
        var simulationPath = Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs");
        var simulation = File.ReadAllText(simulationPath);
        var servicePath = Path.Combine(
            repository, "Services/StrategyPreview/PatternPreviewService.cs");

        File.ReadAllLines(endpointPath).Length.Should().BeLessThanOrEqualTo(160);
        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(250);
        File.ReadAllLines(simulationPath).Length.Should().BeLessThanOrEqualTo(500);
        endpoint.Should().Contain("IPatternPreviewService preview");
        endpoint.Should().NotContain("LongPositionExecutionPolicy");
        endpoint.Should().NotContain("StrategyCompiler.Compile");
        endpoint.Should().NotContain("IOhlcvRepository");
        simulation.Should().NotContain("StockTrader.Data");
        simulation.Should().NotContain("StockTrader.Services");
        simulation.Should().NotContain("DateTime.UtcNow");
        simulation.Should().NotContain("IResult");
    }

    [Fact]
    public void StrategyWarmupRequirementHasOneCatalogOwner()
    {
        var repository = FindRepositoryRoot();
        var owner = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/StrategyEvaluationPolicy.cs"));
        var backtest = File.ReadAllText(Path.Combine(
            repository, "Application/Backtesting/PreparedBacktestData.cs"));
        var preview = File.ReadAllText(Path.Combine(
            repository, "Services/StrategyPreview/PatternPreviewService.cs"));
        var detector = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/RuleBasedDetector.cs"));
        var conditions = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/RuleConditionEvaluator.cs"));
        var preparer = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestDataPreparer.cs"));
        var liveExit = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LivePositionExitEvaluator.cs"));

        owner.Should().Contain("public const int MinimumWarmupBars = 50;");
        backtest.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        preview.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        detector.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        conditions.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        owner.Should().Contain("public const int EntryAtrPeriod = 14;");
        detector.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        preparer.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        preview.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        liveExit.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
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
            repository, "Services/Order/LivePositionExitEvaluator.cs"));

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
            repository, "Services/Order/LivePositionExitEvaluator.cs"));

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
        var preview = File.ReadAllText(Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));
        var engine = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSimulationEngine.cs"));
        var entryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestSignalEntryProcessor.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestExecutionAdapter.cs"));
        var live = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LivePositionExitEvaluator.cs"));

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
        var evaluatorPath = Path.Combine(
            repository, "Services/Order/LivePositionExitEvaluator.cs");
        var evaluator = File.ReadAllText(evaluatorPath);

        File.ReadAllLines(Path.Combine(
            repository, "BackgroundServices/PositionExitManagerService.cs"))
            .Length.Should().BeLessThanOrEqualTo(250);
        File.ReadAllLines(evaluatorPath).Length.Should().BeLessThanOrEqualTo(300);
        liveManager.Should().Contain("exitEvaluator.EvaluateAsync(");
        liveManager.Should().NotContain("LiveLongPositionDecisionPolicy.Evaluate(");
        evaluator.Should().Contain("LiveLongPositionDecisionPolicy.Evaluate(");
        evaluator.Should().NotContain("position.CurrentPrice <= position.StopLossPrice");
        evaluator.Should().NotContain("position.CurrentPrice >= position.TargetPrice");
        liveManager.Should().NotContain("DateTime.UtcNow");
        liveManager.Should().NotContain("TZConvert");
        liveManager.Should().NotContain("7.0 / 5.0");
        liveManager.Should().Contain("exitCoordinator.SubmitAsync(");
        liveManager.Should().Contain("exitCoordinator.ReconcileAsync(");
        liveManager.Should().NotContain("brokerService.ClosePositionAsync(");
        liveManager.Should().NotContain("ExitOrderReconciliationPolicy.Resolve(");
        liveManager.Should().NotContain("ReleasePositionExitClaimAsync(");
        liveManager.Should().NotContain("TryCompletePositionExitAsync(");
        evaluator.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        evaluator.Should().Contain("StrategyEvaluationPolicy.LiveExitIndicatorLookbackDays");
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
        var portfolio = File.ReadAllText(Path.Combine(repository, "desktop-app/src/pages/Portfolio.svelte"));
        var desktopEndpoints = File.ReadAllText(Path.Combine(repository, "desktop-app/src/api/endpoints.ts"));

        orders.Should().Contain("exits.SubmitAsync(");
        portfolio.Should().Contain("orderApi.closePosition(symbol)");
        desktopEndpoints.Should().Contain("api.post('/api/orders/close-position'");
        orders.Should().NotContain("broker.ClosePositionAsync(");
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

using System.Text.Json;
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
        var workspace = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestWorkspace.js"));
        var workspaceTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestWorkspace.test.js"));
        var factorLabModel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestFactorLab.js"));
        var factorLabModelTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestFactorLab.test.js"));
        var viewModel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestViewModel.js"));
        var viewModelTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestViewModel.test.js"));
        var researchTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestResearch.test.js"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(500);
        page.Should().Contain("from '../features/backtest/backtestResearch'");
        page.Should().Contain("from '../features/backtest/backtestWorkspace'");
        page.Should().Contain("from '../features/backtest/backtestFactorLab'");
        page.Should().Contain("from '../features/backtest/backtestViewModel'");
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
        page.Should().NotContain("symbolsText: 'SPY, QQQ, TQQQ'");
        page.Should().NotContain("function timeframeWarning(");
        page.Should().NotContain("Promise.all(definitions.map(");
        page.Should().NotContain("function getFactorLabRankingRows(");
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
        riskSettings.Should().Contain("bind:value={form.walkForwardInSampleMonths}");
        riskSettings.Should().Contain("min=\"1\" step=\"1\"");
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
        workspace.Should().Contain("export function createBacktestForm(");
        workspace.Should().Contain("export function buildTimeframeWarning(");
        workspaceTests.Should().Contain("reset factories return independent canonical research state");
        factorLabModel.Should().Contain("export async function queryFactorLabCandidates(");
        factorLabModelTests.Should().Contain("preserve API contract and build eligible universe variants");
        viewModel.Should().Contain("export function buildBacktestViewModel(");
        viewModel.Should().Contain("export function buildBacktestResearchPlans(");
        viewModelTests.Should().Contain("stale factor variants never affect scenario estimates");
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
        var endpoints = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/endpoints.ts"));
        var apiTypes = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/types.ts"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(400);
        page.Should().Contain("<OptimizationJobForm");
        page.Should().Contain("<OptimizationJobList");
        page.Should().Contain("buildOptimizationJob(form, pattern)");
        page.Should().NotContain("function parseNumberList(");
        page.Should().NotContain("function getResultInsights(");
        page.Should().NotContain("formatSignedPercent(");
        form.Should().Contain("estimatedCombinationCount(form)");
        jobs.Should().Contain("resultInsights(result, results)");
        endpoints.Should().Contain("id: result.id");
        apiTypes.Should().Contain("id: number;");
        apiTypes.Should().Contain("'Paused'");
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
        var metadata = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternMetadata.js"));
        var metadataTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternMetadata.test.js"));
        var previewModel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternPreviewModel.js"));
        var previewModelTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternPreviewModel.test.js"));
        var persistence = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternPersistence.js"));
        var persistenceTests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternPersistence.test.js"));
        var uiCatalog = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/patternBuilderUiCatalog.js"));
        var workspaceSidebar = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternWorkspaceSidebar.svelte"));
        var strategyTree = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternStrategyTree.svelte"));
        var ruleInspector = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/pattern-builder/PatternRuleInspector.svelte"));

        File.ReadAllLines(pagePath).Length.Should().BeLessThanOrEqualTo(500);
        page.Should().Contain("from '../features/pattern-builder/patternValidation'");
        page.Should().Contain("from '../features/pattern-builder/patternWorkspace'");
        page.Should().Contain("from '../features/pattern-builder/patternMetadata'");
        page.Should().Contain("from '../features/pattern-builder/patternPreviewModel'");
        page.Should().Contain("from '../features/pattern-builder/patternPersistence'");
        page.Should().Contain("from '../features/pattern-builder/patternBuilderUiCatalog'");
        page.Should().NotContain("function collectValidationIssues(");
        page.Should().NotContain("function buildWorkspace(");
        page.Should().NotContain("function buildPatternPayload(");
        page.Should().NotContain("function normalizeRule(");
        page.Should().NotContain("patternApi.list(");
        page.Should().NotContain("patternApi.create(");
        page.Should().NotContain("patternApi.get(");
        page.Should().NotContain("patternApi.update(");
        page.Should().NotContain("patternApi.delete(");
        page.Should().NotContain(".splice(");
        page.Should().NotContain("JSON.parse(JSON.stringify");
        page.Should().NotContain("언제 살까?");
        page.Should().NotContain("선택한 조건 바꾸기");
        page.Should().Contain("collectPatternValidationIssues(workspace");
        page.Should().Contain("projectPatternMetadata(metadata)");
        page.Should().Contain("indicatorFieldConfigs: builderMetadata.indicatorFieldConfigs");
        page.Should().Contain("buildPatternPreviewModel(workspace, selectedNode");
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
        metadata.Should().Contain("export function projectPatternMetadata(");
        metadata.Should().Contain("liveStrategyConstraints");
        metadataTests.Should().Contain("incomplete server metadata fails closed");
        previewModel.Should().Contain("export function buildPatternPreviewModel(");
        previewModel.Should().Contain("export function findSelectedRule(");
        previewModelTests.Should().Contain("preserve the chart explanation contract");
        persistence.Should().Contain("export function createPatternPersistence(");
        persistence.Should().Contain("buildPatternPayload(workspace)");
        persistenceTests.Should().Contain("pattern CRUD preserves API payload and workspace hydration contracts");
        persistenceTests.Should().Contain("malformed responses fail closed");
        uiCatalog.Should().Contain("export const glossaryTooltips");
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
    public void DomainOwnsCoreTradingIdentityWithoutLegacyModelDependencies()
    {
        var repository = FindRepositoryRoot();
        var domain = Path.Combine(repository, "Domain");
        var legacyEnums = Path.Combine(repository, "Models", "Enums");
        var violations = Directory.GetFiles(domain, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(
                "StockTrader.Models",
                StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repository, file))
            .ToArray();

        violations.Should().BeEmpty("Domain은 외부 모델 계층에 의존하면 안 됩니다");
        File.Exists(Path.Combine(domain, "MarketData", "TimeFrame.cs")).Should().BeTrue();
        File.Exists(Path.Combine(domain, "MarketData", "DataSource.cs")).Should().BeTrue();
        File.Exists(Path.Combine(domain, "Strategies", "PatternType.cs")).Should().BeTrue();
        File.Exists(Path.Combine(domain, "Trading", "OrderMode.cs")).Should().BeTrue();
        File.Exists(Path.Combine(legacyEnums, "TimeFrame.cs")).Should().BeFalse();
        File.Exists(Path.Combine(legacyEnums, "DataSource.cs")).Should().BeFalse();
        File.Exists(Path.Combine(repository, "Models", "PatternType.cs")).Should().BeFalse();
        File.Exists(Path.Combine(legacyEnums, "OrderMode.cs")).Should().BeFalse();
    }

    [Fact]
    public void SettingsHttpBoundaryUsesAnApplicationUseCaseAndExplicitContracts()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/SettingsEndpoints.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/SettingsContracts.cs"));
        var service = File.ReadAllText(Path.Combine(
            repository, "Application/Settings/SettingsManagementService.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/SettingsManagementStore.cs"));

        endpoints.Should().Contain("SettingsManagementService service");
        endpoints.Should().Contain("SettingsUpdateRequest request");
        endpoints.Should().NotContain("ISettingsRepository");
        endpoints.Should().NotContain("UserSettings");
        endpoints.Should().NotContain("DateTime.UtcNow");
        contracts.Should().Contain("TelegramBotTokenConfigured");
        contracts.Should().Contain("DiscordWebhookConfigured");
        contracts.Should().Contain("SmtpPasswordConfigured");
        contracts.Should().NotContain("MaskSecret");
        service.Should().Contain("timeProvider.GetUtcNow()");
        service.Should().NotContain("StockTrader.Data");
        service.Should().NotContain("StockTrader.Models");
        store.Should().Contain("ISettingsManagementStore");
        store.Should().Contain("UserSettings");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository, "desktop-app/openapi/stocktrader_desktop.json")));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var responseSchema = schemas.GetProperty("SettingsResponse").GetRawText();
        var requestSchema = schemas.GetProperty("SettingsUpdateRequest").GetRawText();
        responseSchema.Should().NotContain("telegramBotToken\"");
        responseSchema.Should().NotContain("discordWebhookUrl\"");
        responseSchema.Should().NotContain("smtpPassword\"");
        requestSchema.Should().Contain("telegramBotToken");
        requestSchema.Should().Contain("discordWebhookUrl");
        requestSchema.Should().Contain("smtpPassword");
    }

    [Fact]
    public void SymbolProfileAssignmentHasOneApplicationBoundaryAndNoDirectEfConsumers()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/SymbolProfileEndpoints.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/SymbolProfileContracts.cs"));
        var service = File.ReadAllText(Path.Combine(
            repository, "Application/SymbolProfiles/SymbolProfileManagementService.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/SymbolProfileStore.cs"));
        var detection = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/PatternDetectionService.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));

        endpoints.Should().Contain("SymbolProfileManagementService service");
        endpoints.Should().Contain("SymbolProfileUpsertRequest request");
        endpoints.Should().NotContain("AppDbContext");
        endpoints.Should().NotContain("SymbolProfiles.");
        endpoints.Should().NotContain("DateTime.UtcNow");
        contracts.Should().Contain("public sealed record SymbolProfileResponse(");
        service.Should().Contain("PatternCatalog.IsOperationalBuiltIn");
        service.Should().Contain("SymbolProfilePolicy.DefaultRiskPerTradePercent");
        service.Should().Contain("MarketSymbolPolicy.Normalize");
        service.Should().NotContain("StockTrader.Data");
        service.Should().NotContain("StockTrader.Models");
        store.Should().Contain("ISymbolProfileStore");
        store.Should().Contain("ExecuteUpdateAsync(");
        detection.Should().Contain("SymbolProfileManagementService");
        detection.Should().NotContain("AppDbContext");
        detection.Should().NotContain("Microsoft.EntityFrameworkCore");
        registrations.Should().Contain("AddScoped<ISymbolProfileStore, SymbolProfileStore>()");
    }

    [Fact]
    public void ResearchUniverseApisUseExplicitApplicationAndPersistenceBoundaries()
    {
        var repository = FindRepositoryRoot();
        var universeEndpoints = File.ReadAllText(Path.Combine(
            repository, "Api/UniverseEndpoints.cs"));
        var factorEndpoints = File.ReadAllText(Path.Combine(
            repository, "Api/FinancialFactorEndpoints.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/ResearchUniverseContracts.cs"));
        var universeService = File.ReadAllText(Path.Combine(
            repository, "Application/Research/ResearchUniverseQueryService.cs"));
        var factorService = File.ReadAllText(Path.Combine(
            repository, "Application/Research/FinancialFactorQueryService.cs"));
        var importService = File.ReadAllText(Path.Combine(
            repository, "Application/Research/FinancialSnapshotImportService.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/ResearchUniverseStore.cs"));
        var parser = File.ReadAllText(Path.Combine(
            repository, "Services/Financial/FinancialSnapshotFileParser.cs"));
        var collectionWorker = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/FinancialSnapshotIngestionService.cs"));
        var secSync = File.ReadAllText(Path.Combine(
            repository, "Services/Financial/SecFinancialSnapshotSyncService.cs"));
        var collectionStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/FinancialCollectionStore.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/DataServiceExtensions.cs"));

        universeEndpoints.Should().Contain("ResearchUniverseQueryService");
        universeEndpoints.Should().Contain("ResearchUniverseMetaResponse");
        universeEndpoints.Should().NotContain("AppDbContext");
        universeEndpoints.Should().NotContain("Microsoft.EntityFrameworkCore");
        factorEndpoints.Should().Contain("FinancialFactorQueryService");
        factorEndpoints.Should().Contain("FinancialFactorQueryResponse");
        factorEndpoints.Should().NotContain("AppDbContext");
        factorEndpoints.Should().NotContain("Microsoft.EntityFrameworkCore");
        contracts.Should().Contain("FinancialPipelineStatusResponse");
        universeService.Should().NotContain("StockTrader.Data");
        universeService.Should().NotContain("StockTrader.Models");
        factorService.Should().NotContain("StockTrader.Data");
        factorService.Should().NotContain("StockTrader.Models");
        importService.Should().Contain("TimeProvider");
        importService.Should().Contain("MarketSymbolPolicy.Normalize");
        importService.Should().NotContain("DateTime.UtcNow");
        importService.Should().NotContain("AppDbContext");
        store.Should().Contain("IResearchUniverseStore");
        store.Should().Contain("IDbContextFactory<AppDbContext>");
        parser.Should().Contain("FinancialSnapshotImportItem");
        parser.Should().NotContain("StockTrader.Api");
        collectionWorker.Should().Contain("IFinancialCollectionStore");
        collectionWorker.Should().Contain("TimeProvider");
        collectionWorker.Should().NotContain("AppDbContext");
        collectionWorker.Should().NotContain("DateTime.UtcNow");
        secSync.Should().Contain("SecFinancialDocumentParser.Parse");
        secSync.Should().Contain("SecFinancialSnapshotFactory.Create");
        secSync.Should().Contain("IFinancialCollectionStore");
        secSync.Should().Contain("TimeProvider");
        secSync.Should().NotContain("AppDbContext");
        secSync.Should().NotContain("Microsoft.EntityFrameworkCore");
        secSync.Should().NotContain("DateTime.UtcNow");
        collectionStore.Should().Contain("IDbContextFactory<AppDbContext>");
        registrations.Should().Contain(
            "AddSingleton<IResearchUniverseStore, ResearchUniverseStore>()");
        registrations.Should().Contain(
            "AddSingleton<IFinancialCollectionStore, FinancialCollectionStore>()");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository, "desktop-app/openapi/stocktrader_desktop.json")));
        var paths = document.RootElement.GetProperty("paths");
        paths.GetProperty("/api/universe/query").GetRawText()
            .Should().Contain("ResearchUniverseQueryResponse");
        paths.GetProperty("/api/financial-factors/query").GetRawText()
            .Should().Contain("FinancialFactorQueryResponse");
        paths.GetProperty("/api/financial-factors/pipeline/status").GetRawText()
            .Should().Contain("FinancialPipelineStatusResponse");
    }

    [Fact]
    public void ResearchAndLiveBoundariesShareTheMarketSymbolPolicy()
    {
        var repository = FindRepositoryRoot();
        var consumers = new[]
        {
            "Application/Settings/SettingsManagementService.cs",
            "Application/MarketData/DailyMarketDataSyncPolicy.cs",
            "Application/SymbolProfiles/SymbolProfileManagementService.cs",
            "Services/Backtest/BacktestDataPreparer.cs",
            "Services/StrategyPreview/PatternPreviewService.cs",
            "Services/Patterns/PatternDetectionService.cs"
        };

        consumers
            .Where(path => !File.ReadAllText(Path.Combine(repository, path))
                .Contains("MarketSymbolPolicy.", StringComparison.Ordinal))
            .Should().BeEmpty("종목 식별자는 미리보기·백테스트·실시간·설정에서 같아야 합니다");
    }

    [Fact]
    public void DesktopSettingsUsesServerCatalogsAndPureRequestProjection()
    {
        var repository = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(repository, "desktop-app/src/pages/Settings.svelte"));
        var model = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/settings/settingsModel.js"));
        var tests = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/settings/settingsModel.test.js"));

        page.Should().Contain("createSettingsForm(data)");
        page.Should().Contain("buildSettingsRequest(form)");
        page.Should().Contain("form.orderModes as option");
        page.Should().Contain("form.dataProviders as option");
        page.Should().Contain("form.patterns as pattern");
        page.Should().Contain("패턴 빌더 미리보기는 빌더에서 지정한 종목");
        page.Should().NotContain("SignalOnly");
        page.Should().NotContain("YahooFinance");
        page.Should().NotContain("data.OrderMode");
        page.Should().NotContain("data.WatchlistSymbols");
        model.Should().Contain("export function buildSettingsRequest(");
        model.Should().Contain("export function parseWatchlist(");
        tests.Should().Contain("fails closed instead of inventing unsupported defaults");
    }

    [Fact]
    public void PatternDisplayNamesComeFromTheDomainCatalog()
    {
        var repository = FindRepositoryRoot();
        var discord = File.ReadAllText(Path.Combine(
            repository, "Services/Notification/DiscordNotificationChannel.cs"));
        var telegram = File.ReadAllText(Path.Combine(
            repository, "Services/Notification/TelegramNotificationChannel.cs"));
        var metadata = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/StrategyBuilderMetadataResponse.cs"));

        discord.Should().Contain("PatternCatalog.DisplayName(");
        telegram.Should().Contain("PatternCatalog.DisplayName(");
        telegram.Should().NotContain("GetPatternKorean(");
        metadata.Should().Contain("Patterns: PatternCatalog.All.Select(");
    }

    [Fact]
    public void RuleBasedDetectorDelegatesIndicatorMathAndEvaluationCache()
    {
        var repository = FindRepositoryRoot();
        var detectorPath = Path.Combine(repository, "Services/Patterns/RuleBasedDetector.cs");
        var evaluatorPath = Path.Combine(repository, "Services/Patterns/RuleIndicatorEvaluator.cs");
        var contextPath = Path.Combine(repository, "Services/Patterns/RuleIndicatorEvaluationContext.cs");
        var registryPath = Path.Combine(repository, "Services/Patterns/RuleIndicatorCalculatorRegistry.cs");
        var standardPath = Path.Combine(repository, "Services/Patterns/StandardRuleIndicatorCalculators.cs");
        var structurePath = Path.Combine(repository, "Services/Patterns/PriceStructureRuleIndicatorCalculators.cs");
        var momentumPath = Path.Combine(repository, "Services/Patterns/MomentumVolumeRuleIndicatorCalculators.cs");
        var mathPath = Path.Combine(repository, "Services/Patterns/RuleIndicatorMath.cs");
        var conditionPath = Path.Combine(repository, "Services/Patterns/RuleConditionEvaluator.cs");
        var detector = File.ReadAllText(detectorPath);
        var evaluator = File.ReadAllText(evaluatorPath);
        var context = File.ReadAllText(contextPath);
        var registry = File.ReadAllText(registryPath);
        var conditions = File.ReadAllText(conditionPath);
        var evaluatorTests = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/RuleIndicatorEvaluatorTests.cs"));

        File.ReadAllLines(detectorPath).Length.Should().BeLessThanOrEqualTo(500);
        File.ReadAllLines(evaluatorPath).Length.Should().BeLessThanOrEqualTo(80);
        File.ReadAllLines(contextPath).Length.Should().BeLessThanOrEqualTo(100);
        File.ReadAllLines(registryPath).Length.Should().BeLessThanOrEqualTo(100);
        File.ReadAllLines(standardPath).Length.Should().BeLessThanOrEqualTo(260);
        File.ReadAllLines(structurePath).Length.Should().BeLessThanOrEqualTo(230);
        File.ReadAllLines(momentumPath).Length.Should().BeLessThanOrEqualTo(230);
        File.ReadAllLines(mathPath).Length.Should().BeLessThanOrEqualTo(230);
        detector.Should().Contain("_indicatorEvaluator.CreateContext(");
        detector.Should().Contain("new RuleConditionEvaluator(_indicatorEvaluator)");
        conditions.Should().Contain("_indicators.Compute(");
        detector.Should().NotContain("switch (indicator.ToUpperInvariant())");
        detector.Should().NotContain("case \"RSI\"");
        detector.Should().NotContain("ComputeAdx(");
        evaluator.Should().Contain("RuleIndicatorCalculatorRegistry.TryGet(");
        evaluator.Should().NotContain("switch (");
        registry.Should().Contain("IndicatorCatalog.All");
        registry.Should().Contain("StandardRuleIndicatorCalculators.All");
        registry.Should().Contain("PriceStructureRuleIndicatorCalculators.All");
        registry.Should().Contain("MomentumVolumeRuleIndicatorCalculators.All");
        context.Should().Contain("private readonly Dictionary<string, object> _cache");
        evaluatorTests.Should().Contain("CalculatorRegistryCoversEveryCentralCatalogIndicatorExactlyOnce");
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
        detector.Should().Contain("DetectedAt = curr.Timestamp");
        detector.Should().Contain("SignalBarAt = curr.Timestamp");
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
    public void PatternDetectorsUseTheEvaluatedBarAsTheirDeterministicSignalTime()
    {
        var repository = FindRepositoryRoot();
        var detectorFiles = Directory.EnumerateFiles(
                Path.Combine(repository, "Services/Patterns"),
                "*Detector.cs",
                SearchOption.TopDirectoryOnly)
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file => file.Source.Contains("DetectedAt =", StringComparison.Ordinal))
            .ToArray();

        detectorFiles.Should().NotBeEmpty();
        detectorFiles
            .Where(file => file.Source.Contains("DateTime.UtcNow", StringComparison.Ordinal)
                || !file.Source.Contains("SignalBarAt =", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file.Path))
            .Should().BeEmpty(
                "연구·미리보기·백테스트 감지는 실행 시계가 아니라 평가한 봉으로 재현되어야 합니다");
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
        factory.Should().Contain("new RuleBasedDetector(_indicators, strategy)");
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
    public void BuiltInPatternDetectionUsesOneCatalogForRuntimeAndBacktest()
    {
        var repository = FindRepositoryRoot();
        var catalog = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/BuiltInPatternDetectorCatalog.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/PatternServiceExtensions.cs"));
        var backtest = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestService.cs"));

        catalog.Should().Contain("D<Tqqq200SmaDetector>(PatternType.Tqqq200Sma)");
        catalog.Should().Contain("IBuiltInPatternDetectorFactory");
        catalog.Should().NotContain("D<OrbDetector>");
        catalog.Should().NotContain("D<EarningsDriftDetector>");
        registrations.Should().Contain("foreach (var descriptor in BuiltInPatternDetectorCatalog.All)");
        registrations.Should().NotContain("AddScoped<IPatternDetector, GapUpPullbackDetector>");
        backtest.Should().Contain("_builtInDetectors.CreateAll(settings)");
        backtest.Should().NotContain("new GapUpPullbackDetector(");
        backtest.Should().NotContain("new Tqqq200SmaDetector(");
        File.ReadAllLines(Path.Combine(
                repository, "Services/Patterns/BuiltInPatternDetectorCatalog.cs"))
            .Length.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void LivePatternConfigurationUsesOneDatabaseBackedApplicationBoundary()
    {
        var repository = FindRepositoryRoot();
        var application = File.ReadAllText(Path.Combine(
            repository, "Application/Settings/LiveParameterService.cs"));
        var endpoint = File.ReadAllText(Path.Combine(repository, "Api/BacktestEndpoints.cs"));
        var detection = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/PatternDetectionService.cs"));
        var positionCycle = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LivePositionMonitoringCycle.cs"));
        var metadataEndpoint = File.ReadAllText(Path.Combine(
            repository, "Api/MetadataEndpoints.cs"));
        var desktopEndpoints = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/endpoints.ts"));

        application.Should().Contain("ISettingsManagementStore store");
        application.Should().Contain("PatternCatalog.IsOperationalBuiltIn");
        application.Should().NotContain("IWebHostEnvironment");
        application.Should().NotContain("File.WriteAllText");
        application.Should().NotContain("appsettings.json");
        endpoint.Should().Contain("ILiveParameterService liveParameters");
        endpoint.Should().Contain("liveParameters.ApplyAsync(");
        detection.Should().Contain("ILiveParameterService");
        detection.Should().Contain("PatternOverrideMerger.Merge(");
        detection.Should().Contain("_builtInDetectors.CreateAll(patternSettings)");
        detection.Should().NotContain("ISettingsRepository");
        positionCycle.Should().Contain("liveParameters.GetAsync(ct)");
        metadataEndpoint.Should().Contain("Produces<StrategyBuilderMetadataResponse>()");
        desktopEndpoints.Should().Contain(
            "components['schemas']['StrategyBuilderMetadataResponse']");
        desktopEndpoints.Should().NotContain(
            "strategyBuilderMetadataPromise: Promise<any>");
        File.Exists(Path.Combine(
            repository, "Services/LiveParameter/LiveParameterService.cs")).Should().BeFalse();
        File.Exists(Path.Combine(
            repository, "Services/LiveParameter/ILiveParameterService.cs")).Should().BeFalse();
        File.Exists(Path.Combine(
            repository, "docs/architecture/adr/0042-centralize-live-pattern-configuration.md"))
            .Should().BeTrue();
    }

    [Fact]
    public void LiveStrategyExecutionPathsUseCompiledRepositoryBoundary()
    {
        var repository = FindRepositoryRoot();
        var livePaths = new[]
        {
            "Services/Patterns/PatternDetectionService.cs",
            "Services/Signal/SignalService.cs",
            "Services/Order/LivePositionMonitoringCycle.cs"
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
        var apiManifest = File.ReadAllText(Path.Combine(repository, "k8s/deployment-api.yaml"));

        deploy.Should().Contain("scale deployment stocktrader-api --replicas=0");
        deploy.Should().Contain("sqlite3 \"$data_dir/stocktrader.db\" \".backup '$backup_path'\"");
        deploy.Should().Contain("PRAGMA quick_check;");
        deploy.Should().Contain("dotnet StockTrader.dll --migrate-database");
        var migrationIndex = deploy.IndexOf("--migrate-database", StringComparison.Ordinal);
        var imageImportIndex = deploy.IndexOf("ctr images import", StringComparison.Ordinal);
        var rolloutIndex = deploy.IndexOf("deployment-api.yaml", StringComparison.Ordinal);
        migrationIndex.Should().BeLessThan(imageImportIndex);
        imageImportIndex.Should().BeLessThan(rolloutIndex);
        deploy.Should().Contain("STOCKTRADER_DATA_DIR:?");
        deploy.Should().Contain("__STOCKTRADER_DATA_DIR__");
        deploy.Should().NotContain("/home/");
        apiManifest.Should().Contain("path: __STOCKTRADER_DATA_DIR__");
        apiManifest.Should().NotContain("/home/");
    }

    [Fact]
    public void PublicDeploymentArtifactsDoNotEmbedPrivateEnvironmentIdentity()
    {
        var repository = FindRepositoryRoot();
        var deploy = File.ReadAllText(Path.Combine(repository, "scripts/deploy-k3s.sh"));
        var desktopManifest = File.ReadAllText(Path.Combine(repository, "k8s/deployment-desktop.yaml"));
        var publicConfiguration = string.Join('\n',
            File.ReadAllText(Path.Combine(repository, "Program.cs")),
            File.ReadAllText(Path.Combine(repository, "appsettings.json")),
            File.ReadAllText(Path.Combine(repository, "Configuration/FinancialDataPipelineSettings.cs")),
            File.ReadAllText(Path.Combine(repository, "DESKTOP_APP_README.md")),
            desktopManifest);

        deploy.Should().Contain("STOCKTRADER_HOST:?");
        deploy.Should().Contain("__STOCKTRADER_HOST__");
        desktopManifest.Should().Contain("host: \"__STOCKTRADER_HOST__\"");
        publicConfiguration.Should().NotMatchRegex(@"(?i)C:\\\\Users\\\\");
        publicConfiguration.Should().NotMatchRegex(@"(?i)[A-Z0-9.-]+\.local\b");
        publicConfiguration.Should().NotMatchRegex(@"(?i)\b(?:10|192\.168)\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
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
        var management = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/CustomPatternManagementService.cs"));
        var migration = Directory.EnumerateFiles(
                Path.Combine(repository, "Data/EfMigrations"), "*_AddStrategyDocumentVersion.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));

        model.Should().Contain("DocumentVersion { get; set; } = StrategyDocumentVersions.Current;");
        versions.Should().Contain("public const int LegacyUnversioned = 0;");
        versions.Should().Contain("public const int Current = 1;");
        policy.Should().Contain("StrategyDocumentVersions.LegacyUnversioned or StrategyDocumentVersions.Current");
        compiler.Should().Contain("StrategyDocumentVersionPolicy.Validate(pattern.DocumentVersion)");
        policy.Should().Contain("StampCurrent(StrategyDocument document)");
        management.Split("StrategyDocumentVersionPolicy.StampCurrent(document)", StringSplitOptions.None)
            .Length.Should().Be(4, "create, update, and backtest promotion must stamp the current document version");
        File.ReadAllText(migration).Should().Contain(
            "defaultValue: StockTrader.Domain.Strategies.StrategyDocumentVersions.Current");
    }

    [Fact]
    public void CustomPatternHttpBoundaryDoesNotBindOrReturnEfEntities()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/CustomPatternEndpoints.cs"));
        var preview = File.ReadAllText(Path.Combine(repository, "Api/PatternPreviewEndpoints.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/CustomPatternContracts.cs"));
        var defaults = File.ReadAllText(Path.Combine(
            repository, "Domain/Strategies/StrategyDocumentDefaults.cs"));
        var model = File.ReadAllText(Path.Combine(repository, "Models/CustomPatternDefinition.cs"));

        endpoints.Should().Contain("CustomPatternWriteRequest request");
        endpoints.Should().Contain("value.ToResponse()");
        endpoints.Should().NotContain("CustomPatternDefinition input");
        endpoints.Should().NotContain("Results.Ok(pattern);");
        preview.Should().Contain("CustomPatternWriteRequest Pattern");
        preview.Should().Contain("request.Pattern.ToStrategyDocument()");
        contracts.Should().Contain("public sealed record CustomPatternResponse(");
        contracts.Should().Contain("internal static class CustomPatternContractMapper");
        contracts.Should().NotContain("public int Id { get; init;");
        contracts.Should().NotContain("public DateTime CreatedAt { get; init;");
        contracts.Should().NotContain("public DateTime UpdatedAt { get; init;");
        defaults.Should().Contain("public static class StrategyDocumentDefaults");
        contracts.Should().Contain("StrategyDocumentDefaults.EmptyListJson");
        model.Should().Contain("StrategyDocumentDefaults.EmptyListJson");
        var metadata = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/StrategyBuilderMetadataResponse.cs"));
        var desktop = File.ReadAllText(Path.Combine(repository, "desktop-app/src/api/endpoints.ts"));
        metadata.Should().Contain("DocumentVersion: StrategyDocumentVersions.Current");
        desktop.Should().Contain("metadata.documentVersion");
        desktop.Should().NotContain("documentVersion: 1");
    }

    [Fact]
    public void CustomPatternHttpBoundaryDelegatesPersistenceAndValidationToApplication()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/CustomPatternEndpoints.cs"));
        var management = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/CustomPatternManagementService.cs"));
        var port = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/ICustomPatternStore.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/CustomPatternStore.cs"));
        var dbContext = File.ReadAllText(Path.Combine(repository, "Data/AppDbContext.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/CustomPatternContracts.cs"));

        File.ReadAllLines(Path.Combine(repository, "Api/CustomPatternEndpoints.cs"))
            .Length.Should().BeLessThanOrEqualTo(80);
        endpoints.Should().Contain("CustomPatternManagementService service");
        endpoints.Should().NotContain("AppDbContext");
        endpoints.Should().NotContain("Microsoft.EntityFrameworkCore");
        endpoints.Should().NotContain("StrategyCompiler.Compile");
        endpoints.Should().NotContain("TimeProvider");
        management.Should().Contain("StrategyCompiler.Compile(input)");
        management.Should().Contain("_store.NameExistsAsync(");
        management.Should().Contain("CustomPatternWriteResult.NameConflict");
        management.Should().Contain("_clock.GetUtcNow()");
        management.Should().Contain("ApplyBacktestAsync(");
        port.Should().Contain("public interface ICustomPatternStore");
        port.Should().Contain("Task<IReadOnlyList<StoredStrategy>> ListAsync");
        port.Should().Contain("Task<CustomPatternStoreWriteOutcome> AddAsync(StoredStrategy strategy");
        port.Should().NotContain("CustomPatternDefinition");
        port.Should().NotContain("Microsoft.EntityFrameworkCore");
        management.Should().NotContain("CustomPatternDefinition");
        adapter.Should().Contain("class CustomPatternStore : ICustomPatternStore");
        adapter.Should().Contain("ExecuteDeleteAsync");
        adapter.Should().Contain("IsNormalizedNameConflict");
        dbContext.Should().Contain("HasIndex(p => p.NormalizedName).IsUnique()");
        contracts.Should().NotContain("NormalizedName");
        var autoTune = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationAutoTuneService.cs"));
        autoTune.Should().Contain("CustomPatternManagementService");
        autoTune.Should().Contain("_patterns.UpdateAsync(");
        autoTune.Should().NotContain("db.CustomPatterns");
        autoTune.Should().NotContain("CopyPatternValues(");
        autoTune.Should().NotContain("DateTime.UtcNow");

        var applicationSources = string.Join("\n", Directory.EnumerateFiles(
                Path.Combine(repository, "Application"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        applicationSources.Should().NotContain("CustomPatternDefinition",
            "EF strategy entities must stop at the Data adapter boundary");
    }

    [Fact]
    public void DesktopStrategyContractsComeFromSideEffectFreeBuildTimeOpenApi()
    {
        var repository = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(repository, "StockTrader.csproj"));
        var program = File.ReadAllText(Path.Combine(repository, "Program.cs"));
        var backgroundRegistration = File.ReadAllText(Path.Combine(
            repository, "Extensions/BackgroundServiceExtensions.cs"));
        var package = File.ReadAllText(Path.Combine(repository, "desktop-app/package.json"));
        var desktopTypes = File.ReadAllText(Path.Combine(repository, "desktop-app/src/api/types.ts"));
        var generated = File.ReadAllText(Path.Combine(repository, "desktop-app/src/api/generated.ts"));
        var openApi = File.ReadAllText(Path.Combine(
            repository, "desktop-app/openapi/stocktrader_desktop.json"));
        using var document = JsonDocument.Parse(openApi);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        project.Should().Contain("<OpenApiGenerateDocuments>true</OpenApiGenerateDocuments>");
        project.Should().Contain("Microsoft.Extensions.ApiDescription.Server");
        project.Should().Contain("Microsoft.OpenApi\" Version=\"2.7.5\"");
        program.Should().Contain("GetDocument.Insider");
        program.Should().Contain("includeHostedServices: !isOpenApiGeneration");
        program.Should().Contain("persistDataProtectionKeys: !isOpenApiGeneration");
        var security = File.ReadAllText(Path.Combine(
            repository, "Extensions/SecurityServiceExtensions.cs"));
        security.Should().Contain("UseEphemeralDataProtectionProvider()");
        program.Should().Contain("if (!isOpenApiGeneration)");
        program.Should().Contain("await app.InitializeStockTraderAsync()");
        backgroundRegistration.Should().Contain("if (!includeHostedServices)");
        package.Should().Contain("\"api:generate\"");
        package.Should().Contain("\"api:check\"");
        desktopTypes.Should().Contain("components['schemas']['CustomPatternResponse']");
        desktopTypes.Should().Contain("components['schemas']['CustomPatternWriteRequest']");
        desktopTypes.Should().NotContain("interface CustomPatternDocument");
        generated.Should().Contain("CustomPatternResponse:");
        generated.Should().Contain("CustomPatternWriteRequest:");
        openApi.Should().Contain("\"CustomPatternResponse\"");
        openApi.Should().Contain("\"CustomPatternWriteRequest\"");
        schemas.GetProperty("CustomPatternResponse").GetRawText()
            .Should().NotContain("normalizedName");
        schemas.GetProperty("CustomPatternWriteRequest").GetRawText()
            .Should().NotContain("normalizedName");
        schemas.TryGetProperty("CustomPatternDefinition", out _).Should().BeFalse();
        schemas.GetProperty("ExecuteSignalRequest").GetRawText()
            .Should().Contain("signalId");
        schemas.GetProperty("OrderErrorResponse").GetRawText()
            .Should().Contain("error");
        var strategyDocument = schemas.GetProperty("StrategyDocument").GetRawText();
        strategyDocument.Should().Contain("storedStrategyId");
        strategyDocument.Should().NotContain("normalizedName");
        strategyDocument.Should().NotContain("createdAt");
        strategyDocument.Should().NotContain("updatedAt");
        schemas.GetProperty("BacktestRequest").GetRawText()
            .Should().Contain("#/components/schemas/StrategyDocument");
        schemas.GetProperty("OptimizeRequest").GetRawText()
            .Should().Contain("#/components/schemas/StrategyDocument");
    }

    [Fact]
    public void StrategyExecutionAndResearchUseAStorageIndependentDocument()
    {
        var repository = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/StrategyDocument.cs"));
        var compiled = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/CompiledStrategy.cs"));
        var compiler = File.ReadAllText(Path.Combine(
            repository, "Application/Strategies/StrategyCompiler.cs"));
        var detector = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/ICustomStrategyDetector.cs"));
        var backtest = File.ReadAllText(Path.Combine(repository, "Models/BacktestResult.cs"));
        var optimization = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationModels.cs"));
        var variants = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/StrategyVariantFactory.cs"));
        var persistenceMapper = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/StoredStrategyMapper.cs"));
        var codec = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizeRequestJsonCodec.cs"));
        var jobEndpoints = File.ReadAllText(Path.Combine(repository, "Api/OptimizeJobEndpoints.cs"));
        var jobManagement = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationJobManagementService.cs"));
        var jobExecutor = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/OptimizationJobExecutor.cs"));
        var autoTune = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationAutoTuneService.cs"));
        var autoTuneStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationAutoTuneStore.cs"));

        document.Should().Contain("public sealed class StrategyDocument");
        document.Should().Contain("public int? StoredStrategyId { get; set; }");
        document.Should().NotContain("NormalizedName");
        document.Should().NotContain("CreatedAt");
        document.Should().NotContain("UpdatedAt");
        compiled.Should().Contain("StrategyDocument Source");
        compiled.Should().NotContain("CustomPatternDefinition Source");
        compiler.Should().Contain("Compile(StrategyDocument pattern)");
        compiler.Should().NotContain("Compile(CustomPatternDefinition pattern)");
        detector.Should().Contain("StrategyDocument Definition");
        detector.Should().NotContain("CustomPatternDefinition Definition");
        backtest.Should().Contain("List<StrategyDocument>? CustomPatterns");
        optimization.Should().Contain("StrategyDocument BasePattern");
        variants.Should().Contain("return src.Copy();");
        variants.Should().NotContain("return new StrategyDocument");
        persistenceMapper.Should().Contain("CustomPatternDefinition ToEntity(this StoredStrategy value)");
        persistenceMapper.Should().Contain("StoredStrategy ToStoredStrategy(this CustomPatternDefinition value)");
        codec.Should().Contain("TryGetProperty(basePattern, \"id\"");
        codec.Should().Contain("request.BasePattern.StoredStrategyId = id");
        jobManagement.Should().Contain("OptimizeRequestJsonCodec.Serialize(");
        jobEndpoints.Should().NotContain("OptimizeRequestJsonCodec.Serialize(");
        jobExecutor.Should().Contain("OptimizeRequestJsonCodec.Deserialize(");
        autoTune.Should().Contain("IOptimizationAutoTuneStore");
        autoTune.Should().NotContain("JsonSerializer");
        autoTuneStore.Should().Contain("OptimizeRequestJsonCodec.Deserialize(");
        autoTuneStore.Should().Contain("OptimizeRequestJsonCodec.Serialize(");
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
        var evaluator = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/OptimizationCandidateEvaluator.cs"));

        service.Should().NotContain("using StockTrader.Api");
        service.Should().Contain("_optimization.RunAsync(");
        service.Should().NotContain("StrategyVariantFactory.CloneStrategyDocument(");
        service.Should().NotContain("StrategyOptimizationSpace.GenerateOptimizeCombinations(");
        optimization.Should().NotContain("private static StrategyDocument CloneStrategyDocument(");
        optimization.Should().NotContain("private static void ApplyOptimizeOverrides(");
        optimization.Should().NotContain("private static List<OptimizeParamSnapshot> GenerateOptimizeCombinations(");
        optimization.Should().NotContain("StrategyVariantFactory.CloneStrategyDocument(");
        evaluator.Should().Contain("StrategyVariantFactory.CloneStrategyDocument(");
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
        var walkForwardPath = Path.Combine(repository, "Services/Backtest/WalkForwardAnalysisRunner.cs");
        var service = File.ReadAllText(servicePath);
        var engine = File.ReadAllText(enginePath);
        var runner = File.ReadAllText(runnerPath);
        var walkForward = File.ReadAllText(walkForwardPath);
        var walkForwardPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Backtesting/WalkForwardAnalysisPolicy.cs"));
        var tradeLedger = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestTradeLedger.cs"));

        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(500);
        service.Should().Contain("_simulationEngine.RunAsync(");
        service.Should().Contain("_walkForward.RunAsync(");
        service.Should().NotContain("new WalkForwardWindow");
        service.Should().NotContain("WalkForwardEfficiency =");
        service.Should().NotContain("private async Task<BacktestResult> RunSimulationAsync(");
        service.Should().NotContain("volatilityFactor");
        runner.Should().Contain("_dataPreparer.Slice(");
        runner.Should().Contain("_simulation.RunAsync(");
        File.ReadAllLines(runnerPath).Length.Should().BeLessThanOrEqualTo(150);
        File.ReadAllLines(walkForwardPath).Length.Should().BeLessThanOrEqualTo(150);
        engine.Should().Contain("new BacktestTradeLedger(");
        tradeLedger.Should().Contain("new BacktestExecutionCostLedger(");
        walkForward.Should().Contain("WalkForwardAnalysisPolicy.BuildPlan(");
        walkForward.Should().Contain("WalkForwardAnalysisPolicy.Aggregate(windows)");
        walkForward.Should().Contain("request.WeightStrategy");
        walkForward.Should().Contain("_simulation.RunAsync(");
        walkForward.Should().NotContain("while (");
        walkForwardPolicy.Should().Contain("outOfSampleStart.AddDays(-1)");
        walkForwardPolicy.Should().Contain("nextWindowStart.AddDays(-1)");
        walkForwardPolicy.Should().NotContain("StockTrader.Services");
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
        var exitProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPositionExitProcessor.cs"));
        var pendingEntryProcessor = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestPendingEntryProcessor.cs"));
        var instructionResolver = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestStrategyExecutionInstructionResolver.cs"));
        var executionSession = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LongPositionExecutionSessionPolicy.cs"));
        var scalingPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LongPositionScalingPolicy.cs"));
        var ruleRuntime = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/RuleBasedDetector.cs"));

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
        preview.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
        executionAdapter.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
        executionSession.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        executionSession.Should().Contain("LongPositionScalingPolicy.Apply(");
        executionSession.Should().Contain("LongPositionScalingPolicy.RegisterExecution(");
        exitProcessor.Should().Contain("BacktestStrategyExecutionInstructionResolver.Resolve(");
        pendingEntryProcessor.Should().Contain("BacktestStrategyExecutionInstructionResolver.Resolve(");
        instructionResolver.Should().Contain("detector.EvaluateScaling(");
        instructionResolver.Should().Contain("PositionScaleInCapacityPolicy.CalculateMaxPositionCost(");
        preview.Should().NotContain("LongPositionScalingPolicy.Apply(");
        preview.Should().NotContain("LongPositionScalingPolicy.RegisterExecution(");
        exitProcessor.Should().NotContain("LongPositionScalingPolicy.Apply(");
        exitProcessor.Should().NotContain("LongPositionScalingPolicy.RegisterExecution(");
        pendingEntryProcessor.Should().NotContain("LongPositionScalingPolicy.Apply(");
        exitProcessor.Should().NotContain("_positionScaleCounts");
        preview.Should().NotContain("Math.Round(position.InitialQuantity");
        exitProcessor.Should().NotContain("position.Quantity * scaling.Percent");
        scalingPolicy.Should().Contain("Math.Floor(rawQuantity)");
        ruleRuntime.Should().NotContain("scaleCounts[i] =");
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
        live.Should().Contain("StrategyHistoricalCooldownPolicy.Evaluate(");
        live.Should().Contain("StrategyDrawdownPolicy.EvaluateHistory(");
        live.Should().NotContain("EvaluateLiveCooldowns(");
        live.Should().NotContain("ComputeStrategyDrawdown(");
        live.Should().NotContain("AddTradingDays(");
        live.Should().NotContain("DateTime.UtcNow");
    }

    [Fact]
    public void LiveSignalEvaluationUsesAnApplicationSnapshotInsteadOfEfEntities()
    {
        var repository = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repository, "Services/Signal/SignalService.cs"));
        var port = File.ReadAllText(Path.Combine(
            repository, "Application/Signals/ILiveSignalEvaluationStore.cs"));
        var completedTrade = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/StrategyCompletedTrade.cs"));
        var cooldownPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/StrategyTradeTransitionPolicy.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/LiveSignalEvaluationStore.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/DataServiceExtensions.cs"));

        service.Should().Contain("ILiveSignalEvaluationStore");
        service.Should().Contain("evaluation.CompletedTradesFor(");
        service.Should().NotContain("AppDbContext");
        service.Should().NotContain("Microsoft.EntityFrameworkCore");
        service.Should().NotContain(".TradeRecords");
        service.Should().NotContain(".Positions");
        service.Should().NotContain(".TradeRecommendations");
        service.Should().NotContain(".Tickers");
        port.Should().Contain("LiveSignalEvaluationSnapshot");
        port.Should().NotContain("StockTrader.Models");
        completedTrade.Should().NotContain("StockTrader.Models");
        cooldownPolicy.Should().Contain("IReadOnlyList<StrategyCompletedTrade>");
        cooldownPolicy.Should().NotContain("IReadOnlyList<TradeRecord>");
        adapter.Should().Contain("ILiveSignalEvaluationStore");
        adapter.Should().Contain("AsNoTracking()");
        registrations.Should().Contain(
            "AddScoped<ILiveSignalEvaluationStore, LiveSignalEvaluationStore>()");
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
        var preview = File.ReadAllText(Path.Combine(
            repository, "Application/StrategyPreview/PatternPreviewSimulationEngine.cs"));
        var transitionPolicyPath = Path.Combine(
            repository, "Application/Execution/StrategyTradeTransitionPolicy.cs");
        var transitionPolicy = File.ReadAllText(transitionPolicyPath);
        registry.Should().Contain("StrategyTradeTransitionPolicy.Apply(");
        registry.Should().Contain("StrategyDrawdownPolicy.Observe(");
        preview.Should().Contain("StrategyTradeTransitionPolicy.Apply(");
        preview.Should().Contain("StrategyDrawdownPolicy.Observe(");
        preview.Should().NotContain("consecutiveLosses++");
        preview.Should().NotContain("peakCompoundedReturn");
        transitionPolicy.Should().NotContain("StockTrader.Services");
        File.ReadAllLines(transitionPolicyPath).Length.Should().BeLessThanOrEqualTo(180);
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
    public void OptimizationAdaptersDelegateCandidateEvaluationAndMarketDataPreparation()
    {
        var repository = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repository, "BackgroundServices/OptimizationJobExecutor.cs"));
        var worker = File.ReadAllText(Path.Combine(repository, "BackgroundServices/ContinuousOptimizationService.cs"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationJobExecutionPolicy.cs"));
        var assumptions = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationBacktestAssumptions.cs"));
        var synchronous = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestOptimizationService.cs"));
        var evaluationPort = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/IOptimizationCandidateEvaluator.cs"));
        var evaluator = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/OptimizationCandidateEvaluator.cs"));
        var projection = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationResultProjection.cs"));
        var preparationPort = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/IOptimizationEvaluationContextPreparer.cs"));
        var preparer = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/OptimizationEvaluationContextPreparer.cs"));
        var providerCatalog = File.ReadAllText(Path.Combine(
            repository, "Domain/MarketData/DataProviderCatalog.cs"));
        var backtest = File.ReadAllText(Path.Combine(
            repository, "Services/Backtest/BacktestService.cs"));
        var executionStorePort = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/IOptimizationJobExecutionStore.cs"));
        var executionStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationJobExecutionStore.cs"));
        var lifecyclePort = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/IOptimizationJobLifecycle.cs"));
        var lifecycle = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationJobLifecycle.cs"));
        var resultPersistence = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationResultPersistence.cs"));
        var controlUseCase = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationJobControlService.cs"));
        var controlStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationJobControlStore.cs"));
        var jobEndpoints = File.ReadAllText(Path.Combine(
            repository, "Api/OptimizeJobEndpoints.cs"));
        var initialization = File.ReadAllText(Path.Combine(
            repository, "Extensions/ApplicationInitializationExtensions.cs"));
        var managementUseCase = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationJobManagementService.cs"));
        var managementStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationJobManagementStore.cs"));
        var jobApiMapper = File.ReadAllText(Path.Combine(
            repository, "Api/OptimizationJobApiMapper.cs"));
        var optimizationModels = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationModels.cs"));
        var autoTune = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationAutoTuneService.cs"));
        var autoTuneStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OptimizationAutoTuneStore.cs"));

        source.Should().Contain("IOptimizationEvaluationContextPreparer");
        source.Should().Contain("IOptimizationJobExecutionStore");
        synchronous.Should().Contain("IOptimizationEvaluationContextPreparer");
        source.Should().NotContain("BacktestDataPreparer");
        source.Should().NotContain("IDataFeedServiceFactory");
        source.Should().NotContain("ICustomStrategyDetectorFactory");
        source.Should().NotContain("IOptimizationRepository");
        source.Should().NotContain("JsonSerializer");
        source.Should().NotContain("ParamsJson");
        source.Should().NotContain("new OptimizationResult");
        synchronous.Should().NotContain("BacktestDataPreparer");
        synchronous.Should().NotContain("IDataFeedServiceFactory");
        preparationPort.Should().Contain("OptimizationPreparationResult");
        preparationPort.Should().Contain("OptimizationDataPreparationPolicy");
        preparer.Should().Contain("BacktestDataPreparer");
        preparer.Should().Contain("BacktestRegimeMapBuilder");
        preparer.Should().Contain("_dataFeeds.SelectAsync(request.DataSource, ct)");
        preparer.Should().Contain("DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source)");
        providerCatalog.Should().Contain("UnitedStatesRegimeBenchmark = \"SPY\"");
        providerCatalog.Should().Contain("KoreaRegimeBenchmark = \"069500\"");
        backtest.Should().Contain("_dataFeedFactory.SelectAsync(request.DataSource, ct)");
        backtest.Should().Contain("DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source)");
        backtest.Should().NotContain("request.DataSource == DataSource.LsSecurities");
        executionStorePort.Should().Contain("SaveChunkAsync(");
        executionStorePort.Should().Contain("SaveOutOfSampleAsync(");
        executionStorePort.Should().NotContain("StockTrader.Models");
        executionStorePort.Should().NotContain("StockTrader.Data");
        executionStore.Should().Contain("JsonSerializer.Serialize(item.Params)");
        executionStore.Should().Contain("OptimizationResultPersistence.MergeRankedAsync(");
        executionStore.Should().Contain("BeginTransactionAsync()");
        executionStore.Should().Contain("ExecuteUpdateAsync(");
        lifecyclePort.Should().Contain("OptimizationJobExecutionTicket");
        lifecyclePort.Should().Contain("TryStartNextAsync(");
        lifecyclePort.Should().NotContain("StockTrader.Models");
        lifecyclePort.Should().NotContain("StockTrader.Data");
        lifecycle.Should().Contain("OptimizationJobStatus.Completed");
        lifecycle.Should().Contain("OptimizationJobStatus.Cancelled");
        lifecycle.Should().Contain("ExecuteUpdateAsync(update => update");
        lifecycle.Should().Contain("job.Status == OptimizationJobStatus.Pending");
        lifecycle.Should().Contain("job.Status == OptimizationJobStatus.Running");
        lifecycle.Should().Contain("OptimizationJobStatus.Running");
        lifecycle.Should().NotContain("IOptimizationRepository");
        resultPersistence.Should().Contain("MergeRankedAsync(");
        resultPersistence.Should().Contain("topResultsToKeep");
        controlUseCase.Should().Contain("OptimizationJobControlPolicy.Resolve(");
        controlUseCase.Should().Contain("IOptimizationJobControlStore");
        controlUseCase.Should().NotContain("StockTrader.Models");
        controlUseCase.Should().NotContain("StockTrader.Data");
        controlStore.Should().Contain("ExecuteUpdateAsync(");
        controlStore.Should().Contain("job.Id == jobId && job.Status == from");
        jobEndpoints.Should().Contain("OptimizationJobControlService controls");
        jobEndpoints.Should().Contain("clock.GetUtcNow().UtcDateTime");
        jobEndpoints.Should().NotContain("DateTime.UtcNow");
        jobEndpoints.Should().NotContain("job.Status = OptimizationJobStatus.Paused");
        jobEndpoints.Should().NotContain("job.Status = OptimizationJobStatus.Cancelled");
        jobEndpoints.Should().Contain("OptimizationJobManagementService jobs");
        jobEndpoints.Should().NotContain("IOptimizationRepository");
        jobEndpoints.Should().NotContain("StockTrader.Data");
        jobEndpoints.Should().NotContain("StockTrader.Models");
        jobEndpoints.Should().NotContain("JsonSerializer");
        jobEndpoints.Should().NotContain("CalculateTotalCombinations(");
        File.ReadAllLines(Path.Combine(repository, "Api/OptimizeJobEndpoints.cs"))
            .Length.Should().BeLessThanOrEqualTo(250);
        File.ReadAllLines(Path.Combine(
                repository,
                "Application/Optimization/OptimizationJobManagementService.cs"))
            .Length.Should().BeLessThanOrEqualTo(400);
        File.ReadAllLines(Path.Combine(
                repository,
                "Data/Repositories/OptimizationJobManagementStore.cs"))
            .Length.Should().BeLessThanOrEqualTo(250);
        managementUseCase.Should().Contain("IOptimizationJobManagementStore");
        managementUseCase.Should().Contain("OptimizationCombinationCountPolicy.Calculate(");
        managementUseCase.Should().NotContain("StockTrader.Data");
        managementUseCase.Should().NotContain("StockTrader.Models");
        managementUseCase.Should().NotContain("DateTime.UtcNow");
        managementStore.Should().Contain("JsonSerializer.Deserialize<OptimizeParamSnapshot>(");
        managementStore.Should().Contain("Id = result.Id");
        managementStore.Should().Contain("ExecuteDeleteAsync(");
        jobApiMapper.Should().Contain("OptimizationJobSummaryView");
        optimizationModels.Should().Contain("public int? Id { get; set; }");
        File.Exists(Path.Combine(repository, "Data/Repositories/OptimizationRepository.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(repository, "Data/Repositories/IOptimizationRepository.cs"))
            .Should().BeFalse();
        File.ReadAllLines(Path.Combine(
                repository, "Data/Repositories/OptimizationJobExecutionStore.cs"))
            .Length.Should().BeLessThanOrEqualTo(180);
        File.ReadAllLines(Path.Combine(
                repository, "Data/Repositories/OptimizationJobLifecycle.cs"))
            .Length.Should().BeLessThanOrEqualTo(120);
        File.ReadAllLines(Path.Combine(
                repository, "Data/Repositories/OptimizationResultPersistence.cs"))
            .Length.Should().BeLessThanOrEqualTo(80);
        autoTune.Should().Contain("IOptimizationAutoTuneStore");
        autoTune.Should().Contain("OptimizationPromotionPolicy.SelectCandidate(");
        autoTune.Should().NotContain("StockTrader.Data");
        autoTune.Should().NotContain("StockTrader.Models");
        autoTune.Should().NotContain("IServiceScopeFactory");
        autoTune.Should().NotContain("IOptimizationRepository");
        autoTune.Should().NotContain("JsonSerializer");
        autoTuneStore.Should().Contain("class OptimizationAutoTuneStore : IOptimizationAutoTuneStore");
        autoTuneStore.Should().Contain("job.AppliedResultCount + 1");
        autoTuneStore.Should().Contain("BeginTransactionAsync(ct)");
        autoTuneStore.Should().Contain("ExecuteDeleteAsync(ct)");
        worker.Should().Contain("GetRequiredService<OptimizationAutoTuneService>()");
        File.ReadAllLines(Path.Combine(
                repository,
                "Application/Optimization/OptimizationAutoTuneService.cs"))
            .Length.Should().BeLessThanOrEqualTo(250);
        File.ReadAllLines(Path.Combine(
                repository,
                "Data/Repositories/OptimizationAutoTuneStore.cs"))
            .Length.Should().BeLessThanOrEqualTo(200);
        initialization.Should().Contain("RecoverInterruptedAsync()");
        initialization.Should().NotContain("IOptimizationRepository");
        source.Should().Contain("OptimizationJobExecutionPolicy.SplitPeriod(");
        source.Should().Contain("OptimizationJobExecutionPolicy.BuildSearchPlan(");
        source.Should().Contain("TimeProvider");
        source.Should().NotContain("DateTime.UtcNow");
        worker.Should().Contain("TimeProvider");
        worker.Should().Contain("IOptimizationJobLifecycle");
        worker.Should().Contain("Task.Delay(PollInterval, _clock, stoppingToken)");
        worker.Should().NotContain("DateTime.UtcNow");
        worker.Should().NotContain("IOptimizationRepository");
        worker.Should().NotContain("StockTrader.Models");
        worker.Should().NotContain("OptimizationJobStatus");
        source.Should().Contain("OptimizationJobExecutionTicket");
        source.Should().NotContain("StockTrader.Models");
        policy.Should().Contain("InitialExplorationFraction = 0.60m");
        policy.Should().Contain("FineSearchSeedCount = 5");
        assumptions.Should().Contain("SlippagePercent = 0.05m");
        assumptions.Should().Contain("CommissionPerTrade = 1.00m");
        source.Should().Contain("IOptimizationCandidateEvaluator");
        synchronous.Should().Contain("IOptimizationCandidateEvaluator");
        evaluationPort.Should().Contain("EvaluateBatchAsync(");
        evaluationPort.Should().Contain("RunAsync(");
        evaluator.Should().Contain("OptimizationBacktestAssumptions.SlippagePercent");
        evaluator.Should().Contain("StrategyVariantFactory.CloneStrategyDocument(");
        evaluator.Should().Contain("BacktestPreparedSimulationRunner");
        projection.Should().Contain("TotalReturnPercent * 100");
        projection.Should().Contain("OverallWinRate * 100");
        synchronous.Should().Contain("OptimizationJobExecutionPolicy.SplitPeriod(");
        synchronous.Should().Contain("OptimizationJobExecutionPolicy.BuildSearchPlan(");
        synchronous.Should().NotContain("Math.Clamp(request.OosPercent");
        synchronous.Should().NotContain("StrategyVariantFactory.CloneStrategyDocument(");
        source.Should().NotContain("StrategyVariantFactory.CloneStrategyDocument(");
        source.Should().NotContain("RunCoreWithPreloadedDataAsync(");
        source.Should().NotContain("TotalReturnPercent * 100");
        source.Should().NotContain("0.05m, 1.00m");
        File.ReadAllLines(Path.Combine(
                repository, "BackgroundServices/OptimizationJobExecutor.cs"))
            .Length.Should().BeLessThanOrEqualTo(350);
        File.ReadAllLines(Path.Combine(
                repository, "BackgroundServices/ContinuousOptimizationService.cs"))
            .Length.Should().BeLessThanOrEqualTo(200);
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
        var executionSession = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LongPositionExecutionSessionPolicy.cs"));
        var liveExecution = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveLongPositionExecutionAdapter.cs"));
        var order = File.ReadAllText(Path.Combine(repository, "Services/Order/OrderService.cs"));
        var manualOrder = File.ReadAllText(Path.Combine(
            repository, "Services/Order/ManualOrderWorkflow.cs"));
        var entryExecution = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LiveEntryExecutionCoordinator.cs"));
        var entryStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/LiveEntryExecutionStore.cs"));
        var brokerPort = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/IBrokerService.cs"));
        var entryEvidence = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveEntryOrderEvidencePolicy.cs"));
        var liveEntry = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveEntryPositionFactory.cs"));
        var parity = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/CustomStrategyExecutionParityTests.cs"));
        var executionParity = File.ReadAllText(Path.Combine(
            repository, "tests/StockTrader.Tests/LongPositionExecutionParityTests.cs"));

        preview.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
        backtest.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
        liveExecution.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
        executionSession.Should().Contain("LongPositionExecutionPolicy.Evaluate(");
        preview.Should().NotContain("LongPositionExecutionPolicy.Evaluate(");
        backtest.Should().NotContain("LongPositionExecutionPolicy.Evaluate(");
        liveExecution.Should().NotContain("LongPositionExecutionPolicy.Evaluate(");
        preview.Should().Contain("LongEntryFillPolicy.Reprice(");
        entryExecution.Should().Contain("LiveEntryPositionFactory.CreateFromFill(");
        entryExecution.Should().NotContain("BrokerPositionConfirmation.WaitForAsync(");
        entryExecution.Should().Contain("store.TryClaimAsync(");
        entryExecution.Should().Contain("store.SetOrderEvidenceAsync(");
        entryExecution.Should().Contain("store.CommitFilledEntryAsync(");
        entryExecution.Should().Contain("SubmitEntryOrderAsync(recommendation, ct)");
        entryExecution.Should().Contain("LiveEntryOrderEvidencePolicy.ValidateAcceptedOrder(");
        entryEvidence.Should().Contain("order.Direction != TradeDirection.Long");
        entryEvidence.Should().Contain("order.Quantity != recommendation.ShareQuantity");
        order.Should().Contain("_entryExecutions.ExecuteAsync(recommendation, account, ct)");
        manualOrder.Should().Contain("_entryExecutions.ExecuteAsync(recommendation, account, ct)");
        order.Should().NotContain("LiveEntryPositionFactory.Create(");
        manualOrder.Should().NotContain("LiveEntryPositionFactory.Create(");
        order.Should().NotContain("SubmitEntryOrderAsync");
        manualOrder.Should().NotContain("SubmitEntryOrderAsync");
        order.Should().NotContain("new Position");
        manualOrder.Should().NotContain("new Position");
        liveEntry.Should().Contain("LongEntryFillPolicy.ReanchorExecutedFill(");
        liveEntry.Should().NotContain("StockTrader.Services");
        liveEntry.Should().NotContain("StockTrader.Data");
        order.Should().Contain("_manualOrders.ExecuteAsync(signalId, ct)");
        manualOrder.Should().Contain("IManualOrderSignalStore");
        manualOrder.Should().NotContain("AppDbContext");
        entryStore.Should().Contain("IDbContextFactory<AppDbContext>");
        entryStore.Should().Contain("BeginTransactionAsync(ct)");
        brokerPort.Should().Contain("Task<BrokerOrder?> SubmitEntryOrderAsync(");
        brokerPort.Should().NotContain("Task<bool> PlaceOrderAsync(");
        File.ReadAllLines(Path.Combine(repository, "Services/Order/OrderService.cs"))
            .Length.Should().BeLessThanOrEqualTo(220);
        File.ReadAllLines(Path.Combine(repository, "Services/Order/ManualOrderWorkflow.cs"))
            .Length.Should().BeLessThanOrEqualTo(220);
        entryExecution.Should().Contain("timeProvider.GetUtcNow()");
        order.Should().NotContain("DateTime.UtcNow");
        order.Should().NotContain("actualEntry - stopDistance");
        order.Should().NotContain("actualEntry + targetDistance");
        parity.Should().Contain("PreviewBacktestAndLiveFill_RunTheSameCompiledNextOpenStrategy");
        parity.Should().Contain("PreviewAndBacktest_RunTheSameCompiledFractionalScaleOut");
        parity.Should().Contain("PreviewAndBacktest_ApplyCustomExitOnTheNextOpenEntryBar");
        parity.Should().Contain("PreviewAndBacktest_ApplyScaleOutOnTheNextOpenEntryBar");
        parity.Should().Contain("ScalingStrategy_IsAcceptedForLiveAfterBrokerExecutionParity");
        executionParity.Should().Contain("CommonSessionAndLiveAdapter_AgreeOnPartialProfitIntent");
        parity.Should().Contain("previewEntry.StopPrice.Should().Be(livePosition.StopLossPrice)");
        parity.Should().Contain("liveExecution.Reason.Should().Be(previewExit.Reason)");
        preview.Should().NotContain("current.Low <= position.StopPrice");
        preview.Should().NotContain("current.High >= position.TargetPrice");
    }

    [Fact]
    public void LiveDailyScannerUsesCentralClockAndRegimePolicy()
    {
        var repository = FindRepositoryRoot();
        var scanner = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/PatternScannerService.cs"));
        var cycle = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/LivePatternScanCycle.cs"));
        var regime = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/LiveMarketRegimeEvaluator.cs"));

        cycle.Should().Contain("timeProvider.GetUtcNow()");
        regime.Should().Contain("StrategyEvaluationPolicy.RegimeTrendBars");
        cycle.Should().Contain("StrategyEvaluationPolicy.RegimeLookbackCalendarDays");
        cycle.Should().Contain("StrategyEvaluationPolicy.LiveDailySignalLookbackDays");
        cycle.Should().NotContain("DateTime.UtcNow");
        cycle.Should().NotContain("AddDays(-400)");
        regime.Should().NotContain("SMA(closes, 200)");
    }

    [Fact]
    public void LivePatternScannerIsOnlyAChannelAndResilienceAdapter()
    {
        var repository = FindRepositoryRoot();
        var workerPath = Path.Combine(
            repository, "BackgroundServices/PatternScannerService.cs");
        var cyclePath = Path.Combine(
            repository, "Services/Patterns/LivePatternScanCycle.cs");
        var contractsPath = Path.Combine(
            repository, "Application/Trading/LivePatternScanContracts.cs");

        File.Exists(cyclePath).Should().BeTrue();
        File.Exists(contractsPath).Should().BeTrue();

        var worker = File.ReadAllText(workerPath);
        var cycle = File.ReadAllText(cyclePath);
        var contracts = File.ReadAllText(contractsPath);

        File.ReadAllLines(workerPath).Length.Should().BeLessThanOrEqualTo(120);
        worker.Should().Contain("ILivePatternScanCycle");
        worker.Should().Contain("cycle.RunAsync(symbol, ct)");
        worker.Should().NotContain("IOhlcvRepository");
        worker.Should().NotContain("IDataFeedServiceFactory");
        worker.Should().NotContain("PatternDetectionService");
        worker.Should().NotContain("IPatternSignalStore");
        worker.Should().NotContain("ISignalService");
        worker.Should().NotContain("IOrderService");
        worker.Should().NotContain("IIndicatorService");
        cycle.Should().Contain("ILiveDailyScanData");
        cycle.Should().Contain("ILivePatternDetection");
        cycle.Should().Contain("ILiveSignalProcessor");
        cycle.Should().Contain("ILiveMarketRegimeEvaluator");
        cycle.Should().NotContain("StockTrader.Data");
        cycle.Should().NotContain("DateTime.UtcNow");
        contracts.Should().NotContain("StockTrader.Services");
        contracts.Should().NotContain("StockTrader.Data");
    }

    [Fact]
    public void MarketDataConsumersUseTheProviderOwnedRegimeBenchmark()
    {
        var repository = FindRepositoryRoot();
        var directConsumers = new[]
        {
            "Services/DataFeed/LiveDailyScanData.cs",
            "Services/Analysis/StockAnalysisService.cs",
            "Services/StrategyPreview/PatternPreviewService.cs",
            "Services/ML/MLModelTrainingService.cs",
            "Services/Backtest/BacktestService.cs",
            "Services/Backtest/BacktestRegimeMapBuilder.cs",
            "Services/Backtest/OptimizationEvaluationContextPreparer.cs"
        };

        foreach (var path in directConsumers)
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            source.Should().Contain("DataProviderCatalog");
            source.Should().NotContain("\"SPY\"");
            source.Should().NotContain("\"069500\"");
        }

        var sync = File.ReadAllText(Path.Combine(
            repository, "Services/DataFeed/DailyMarketDataSyncCycle.cs"));
        var ml = File.ReadAllText(Path.Combine(
            repository, "Services/ML/MLModelTrainingService.cs"));
        var regimeClassifier = File.ReadAllText(Path.Combine(
            repository, "Services/ML/MarketRegimeClassifier.cs"));
        var signalScorer = File.ReadAllText(Path.Combine(
            repository, "Services/ML/SignalScorer.cs"));
        sync.Should().Contain("DailyMarketDataSyncPolicy.ResolveRequiredSymbols(");
        sync.Should().Contain("timeProvider.GetUtcNow()");
        sync.Should().NotContain("DateTime.UtcNow");
        ml.Should().Contain("_mlSettings.MinTrainingSamples");
        ml.Should().Contain("_timeProvider.GetUtcNow()");
        ml.Should().NotContain("DateTime.UtcNow");
        regimeClassifier.Should().Contain("_timeProvider.GetUtcNow()");
        regimeClassifier.Should().NotContain("DateTime.UtcNow");
        signalScorer.Should().Contain("_timeProvider.GetUtcNow()");
        signalScorer.Should().NotContain("DateTime.UtcNow");
    }

    [Fact]
    public void DailyDataSyncWorkerOnlySchedulesTheProviderMarketCycle()
    {
        var repository = FindRepositoryRoot();
        var workerPath = Path.Combine(
            repository, "BackgroundServices/DailyDataSyncService.cs");
        var cyclePath = Path.Combine(
            repository, "Services/DataFeed/DailyMarketDataSyncCycle.cs");
        var contractsPath = Path.Combine(
            repository, "Application/MarketData/DailyMarketDataSyncContracts.cs");
        var marketCatalogPath = Path.Combine(
            repository, "Domain/MarketData/MarketRegionCatalog.cs");
        var barStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/OhlcvRepository.cs"));

        var worker = File.ReadAllText(workerPath);
        var cycle = File.ReadAllText(cyclePath);
        var contracts = File.ReadAllText(contractsPath);

        File.ReadAllLines(workerPath).Length.Should().BeLessThanOrEqualTo(110);
        worker.Should().Contain("IDailyMarketDataSyncCycle");
        worker.Should().Contain("PeriodicTimer(interval, timeProvider)");
        worker.Should().NotContain("IOhlcvRepository");
        worker.Should().NotContain("IDataFeedServiceFactory");
        worker.Should().NotContain("ISettingsRepository");
        worker.Should().NotContain("IStatisticsService");
        worker.Should().NotContain("IMarketCalendar");
        cycle.Should().Contain("DataProviderCatalog.Get(source).MarketRegion");
        cycle.Should().Contain("DailyMarketDataSyncPolicy.EvaluateWindow(");
        cycle.Should().Contain("var from = last?.Date");
        cycle.Should().Contain("IsCompletedDailyTimestamp");
        cycle.Should().NotContain("last?.AddDays(1)");
        cycle.Should().NotContain("StockTrader.Data.Repositories");
        cycle.Should().NotContain("IDataFeedService");
        contracts.Should().NotContain("StockTrader.Services");
        contracts.Should().NotContain("StockTrader.Data");
        File.Exists(marketCatalogPath).Should().BeTrue();
        File.Exists(Path.Combine(
            repository, "Services/Market/IMarketCalendar.cs")).Should().BeFalse();
        barStore.Should().Contain("ON CONFLICT(Symbol, TimeFrame, Timestamp) DO UPDATE SET");
        barStore.Should().NotContain("INSERT OR IGNORE INTO OhlcvBars");
    }

    [Fact]
    public void IntradayDataWorkerOnlySchedulesTheProviderMarketCycle()
    {
        var repository = FindRepositoryRoot();
        var workerPath = Path.Combine(
            repository, "BackgroundServices/MarketDataIngestionService.cs");
        var cyclePath = Path.Combine(
            repository, "Services/DataFeed/IntradayMarketDataIngestionCycle.cs");
        var dataPath = Path.Combine(
            repository, "Services/DataFeed/IntradayMarketDataIngestionData.cs");
        var contractsPath = Path.Combine(
            repository, "Application/MarketData/IntradayMarketDataIngestionContracts.cs");
        var streamingPath = Path.Combine(
            repository, "BackgroundServices/AlpacaStreamingService.cs");
        var selectionPath = Path.Combine(
            repository, "Services/DataFeed/RealtimeMarketDataSelectionReader.cs");
        var bufferPath = Path.Combine(
            repository, "Services/Streaming/RealtimeBarIngestionBuffer.cs");
        var sinkPath = Path.Combine(
            repository, "Services/DataFeed/RealtimeBarBatchSink.cs");

        var worker = File.ReadAllText(workerPath);
        var cycle = File.ReadAllText(cyclePath);
        var data = File.ReadAllText(dataPath);
        var contracts = File.ReadAllText(contractsPath);
        var streaming = File.ReadAllText(streamingPath);
        var selection = File.ReadAllText(selectionPath);
        var buffer = File.ReadAllText(bufferPath);
        var sink = File.ReadAllText(sinkPath);

        File.ReadAllLines(workerPath).Length.Should().BeLessThanOrEqualTo(100);
        worker.Should().Contain("IIntradayMarketDataIngestionCycle");
        worker.Should().Contain("PeriodicTimer(");
        worker.Should().Contain("timeProvider");
        worker.Should().Contain("IntradayDataMaxRetries");
        worker.Should().Contain("DataFetchIntervalSeconds");
        worker.Should().NotContain("IOhlcvRepository");
        worker.Should().NotContain("IDataFeedServiceFactory");
        worker.Should().NotContain("ISettingsRepository");
        worker.Should().NotContain("IMarketCalendar");
        worker.Should().NotContain("IStreamingStatusService");
        worker.Should().NotContain("Channel<string>");
        cycle.Should().Contain("DataProviderCatalog.Get(session.Source).MarketRegion");
        cycle.Should().Contain("activeRealtimeSource == session.Source");
        cycle.Should().Contain("connectedRealtimeSource != session.Source");
        cycle.Should().Contain("RealtimeProviderTransition");
        cycle.Should().Contain("errors == session.WatchlistSymbols.Count");
        cycle.Should().Contain("marketCalendar.IsMarketOpen(market)");
        cycle.Should().NotContain("MarketRegion.Korea");
        cycle.Should().NotContain("MarketRegion.UnitedStates");
        cycle.Should().NotContain("StockTrader.Data.Repositories");
        cycle.Should().NotContain("IDataFeedService");
        data.Should().Contain("MarketSymbolPolicy.NormalizeMany");
        data.Should().Contain("TimeFrame.OneMinute");
        File.ReadAllLines(streamingPath).Length.Should().BeLessThanOrEqualTo(400);
        streaming.Should().Contain("IRealtimeBarIngestionBuffer");
        streaming.Should().Contain("IRealtimeMarketDataSelectionReader");
        streaming.Should().Contain("selection.Source != DataSource.Alpaca");
        streaming.Should().Contain("_streamingStatus.MarkConnected()");
        streaming.Should().Contain("_barIngestion.FlushAsync(stoppingToken)");
        streaming.Should().Contain("_barIngestion.ProcessAsync(new OhlcvBar");
        streaming.Should().Contain("flushCancellation.Cancel()");
        streaming.Should().NotContain("IOhlcvRepository");
        streaming.Should().NotContain("Channel<");
        buffer.Should().Contain("_pendingBatch");
        buffer.Should().Contain("_processingLock");
        buffer.Should().Contain("batch retained for retry");
        buffer.Should().Contain("_sink.PersistAndPublishAsync(_pendingBatch, ct)");
        sink.Should().Contain("await repository.AddBarsAsync(bars, ct)");
        sink.Should().Contain("await symbolChannel.Writer.WriteAsync(symbol, ct)");
        sink.IndexOf("await repository.AddBarsAsync(bars, ct)",
                StringComparison.Ordinal)
            .Should().BeLessThan(sink.IndexOf(
                "await symbolChannel.Writer.WriteAsync(symbol, ct)",
                StringComparison.Ordinal));
        streaming.Should().NotContain("ISettingsRepository");
        selection.Should().Contain("MarketSymbolPolicy.NormalizeMany");
        selection.Should().Contain("ISettingsRepository");
        contracts.Should().NotContain("StockTrader.Services");
        contracts.Should().NotContain("StockTrader.Data");
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
        var liveExecution = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LivePositionExecutionEvaluator.cs"));

        owner.Should().Contain("public const int MinimumWarmupBars = 50;");
        backtest.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        preview.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        detector.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        conditions.Should().Contain("StrategyEvaluationPolicy.MinimumWarmupBars");
        owner.Should().Contain("public const int EntryAtrPeriod = 14;");
        detector.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        preparer.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        preview.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        liveExecution.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
    }

    [Fact]
    public void BarAndLiveExecutionShareCloseDecisionPriority()
    {
        var repository = FindRepositoryRoot();
        var barPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LongPositionExecutionPolicy.cs"));
        var livePolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveLongPositionExecutionAdapter.cs"));

        barPolicy.Should().Contain("LongPositionCloseDecisionPolicy.Resolve(");
        livePolicy.Should().Contain("LongPositionExecutionSessionPolicy.Evaluate(");
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
            repository, "Services/Order/LivePositionExecutionEvaluator.cs"));

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
            repository, "Services/Order/LivePositionExecutionEvaluator.cs"));

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
            repository, "Services/Order/LivePositionExecutionEvaluator.cs"));

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
    public void LivePositionManagerDelegatesTradingDecisionsToPurePolicy()
    {
        var repository = FindRepositoryRoot();
        var liveManagerPath = Path.Combine(
            repository, "BackgroundServices/PositionExecutionManagerService.cs");
        var liveManager = File.ReadAllText(liveManagerPath);
        var monitoringPath = Path.Combine(
            repository, "Services/Order/LivePositionMonitoringCycle.cs");
        var monitoring = File.ReadAllText(monitoringPath);
        var monitoringContract = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/ILivePositionMonitoringCycle.cs"));
        var evaluatorPath = Path.Combine(
            repository, "Services/Order/LivePositionExecutionEvaluator.cs");
        var evaluator = File.ReadAllText(evaluatorPath);

        File.ReadAllLines(liveManagerPath).Length.Should().BeLessThanOrEqualTo(80);
        File.ReadAllLines(evaluatorPath).Length.Should().BeLessThanOrEqualTo(300);
        liveManager.Should().Contain("ILivePositionMonitoringCycle");
        liveManager.Should().Contain("cycle.RunAsync(stoppingToken)");
        liveManager.Should().NotContain("IOpenPositionStore");
        liveManager.Should().NotContain("IBrokerService");
        liveManager.Should().NotContain("ILivePositionExecutionCoordinator");
        monitoringContract.Should().NotContain("StockTrader.Services");
        monitoringContract.Should().NotContain("StockTrader.Models");
        monitoring.Should().Contain("executionEvaluator.EvaluateAsync(");
        monitoring.Should().Contain("executionCoordinator.SubmitAsync(");
        monitoring.Should().Contain("executionCoordinator.ReconcileAsync(");
        monitoring.Should().Contain("GetBrokerContextForPositionExitAsync(accountId");
        monitoring.Should().Contain("GetBrokerContextForReconciliationAsync(accountId");
        monitoring.Should().NotContain("GetActiveBrokerServiceAsync(");
        monitoring.Should().NotContain("DateTime.UtcNow");
        monitoring.Should().NotContain("broker.ClosePositionAsync(");
        monitoring.Should().NotContain("PositionOrderReconciliationPolicy.Resolve(");
        monitoring.Should().NotContain("ReleasePositionExecutionClaimAsync(");
        monitoring.Should().NotContain("TryApplyPositionExecutionFillAsync(");
        evaluator.Should().Contain("LiveLongPositionExecutionAdapter.Evaluate(");
        evaluator.Should().Contain("detector.EvaluateScaling(");
        evaluator.Should().Contain("PositionScaleInCapacityPolicy.CalculateMaxPositionCost(");
        evaluator.Should().NotContain("EnablePartialProfit = false");
        evaluator.Should().NotContain("position.CurrentPrice <= position.StopLossPrice");
        evaluator.Should().NotContain("position.CurrentPrice >= position.TargetPrice");
        evaluator.Should().Contain("StrategyEvaluationPolicy.EntryAtrPeriod");
        evaluator.Should().Contain("StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays");
    }

    [Fact]
    public void EntryReconciliationWorkerIsOnlyAClockedSchedulingAdapter()
    {
        var repository = FindRepositoryRoot();
        var workerPath = Path.Combine(
            repository, "BackgroundServices/EntryExecutionReconciliationService.cs");
        var cyclePath = Path.Combine(
            repository, "Services/Order/LiveEntryReconciliationCycle.cs");
        var contractPath = Path.Combine(
            repository, "Application/Execution/ILiveEntryReconciliationCycle.cs");
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));

        File.Exists(cyclePath).Should().BeTrue();
        File.Exists(contractPath).Should().BeTrue();

        var worker = File.ReadAllText(workerPath);
        var cycle = File.ReadAllText(cyclePath);
        var contract = File.ReadAllText(contractPath);

        File.ReadAllLines(workerPath).Length.Should().BeLessThanOrEqualTo(70);
        worker.Should().Contain("ILiveEntryReconciliationCycle");
        worker.Should().Contain("cycle.RunAsync(stoppingToken)");
        worker.Should().Contain("Task.Delay(interval, timeProvider, stoppingToken)");
        worker.Should().NotContain("ILiveEntryExecutionStore");
        worker.Should().NotContain("IAccountManager");
        worker.Should().NotContain("GetOrderHistoryAsync(");
        contract.Should().NotContain("StockTrader.Services");
        contract.Should().NotContain("StockTrader.Models");
        cycle.Should().Contain("GetBrokerContextForReconciliationAsync(");
        cycle.Should().Contain("coordinator.ReconcileAsync(");
        cycle.Should().NotContain("GetActiveBrokerServiceAsync(");
        cycle.Should().NotContain("DateTime.UtcNow");
        registrations.Should().Contain(
            "AddScoped<ILiveEntryReconciliationCycle, LiveEntryReconciliationCycle>");
    }

    [Fact]
    public void PositionApisShareOperationalOrderStatusContract()
    {
        var repository = FindRepositoryRoot();
        var endpointPaths = new[]
        {
            "Api/TradeEndpoints.cs",
            "Api/PortfolioEndpoints.cs"
        };

        foreach (var path in endpointPaths)
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            source.Should().Contain("IOpenPositionQuery");
            source.Should().Contain("OpenPositionResponseMapper.Map");
            source.Should().NotContain("IOpenPositionStore");
            source.Should().NotContain("HoldingDays    = (DateTime.UtcNow");
        }

        var dashboard = File.ReadAllText(Path.Combine(
            repository, "Api/DashboardEndpoints.cs"));
        var dashboardResponse = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/DashboardContracts.cs"));
        dashboard.Should().Contain("IDashboardQuery");
        dashboardResponse.Should().Contain("OpenPositionResponseMapper.Map");

        var orders = File.ReadAllText(Path.Combine(repository, "Api/OrderEndpoints.cs"));
        var contract = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/OpenPositionResponse.cs"));
        var portfolio = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Portfolio.svelte"));
        orders.Should().Contain("/reconcile-position-order");
        orders.Should().NotContain("/reconcile-position-exit");
        orders.Should().Contain("orders.ReconcilePositionAsync(");
        orders.Should().Contain("ExecuteSignalRequest request");
        orders.Should().Contain("StatusCodes.Status500InternalServerError");
        orders.Should().NotContain("JsonDocument.ParseAsync");
        contract.Should().Contain("string OrderStatus");
        contract.Should().Contain("string? OrderKind");
        contract.Should().Contain("bool HasBrokerOrderId");
        contract.Should().NotContain("string ExitStatus");
        contract.Should().NotContain("bool HasExitOrderId");
        contract.Should().Contain("Map(OpenPositionSnapshot position)");
        contract.Should().NotContain("Map(Position position");
        var queryContract = File.ReadAllText(Path.Combine(
            repository, "Application/Portfolio/OpenPositionQueryContracts.cs"));
        var query = File.ReadAllText(Path.Combine(
            repository, "Services/Portfolio/OpenPositionQuery.cs"));
        queryContract.Should().Contain("interface IOpenPositionQuery");
        queryContract.Should().NotContain("StockTrader.Models");
        query.Should().Contain("LivePositionOrderStatusPolicy.Evaluate(position, observedAt)");
        query.Should().Contain("timeProvider.GetUtcNow()");
        query.Should().NotContain("DateTime.UtcNow");
        portfolio.Should().Contain("orderKindLabel(row.orderKind)");
        portfolio.Should().NotContain("row.exitStatus");
    }

    [Fact]
    public void LivePositionExecutionUsesPurposeBuiltAtomicStore()
    {
        var repository = FindRepositoryRoot();
        var coordinatorPath = Path.Combine(
            repository, "Services/Order/LivePositionExecutionCoordinator.cs");
        var coordinator = File.ReadAllText(coordinatorPath);
        var contract = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/PositionExecutionContracts.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/LivePositionExecutionStore.cs"));
        var broadRepositoryPath = Path.Combine(
            repository, "Data/Repositories/TradeRepository.cs");
        var broadContractPath = Path.Combine(
            repository, "Data/Repositories/ITradeRepository.cs");
        var tradingPorts = File.ReadAllText(Path.Combine(
            repository, "Application/Trading/TradingDataPorts.cs"));
        var purposeBuiltStores = new[]
        {
            "TradeHistoryStore.cs",
            "OpenPositionStore.cs",
            "TradeRecommendationStore.cs",
            "PatternSignalStore.cs"
        };

        coordinator.Should().Contain("ILivePositionExecutionStore");
        coordinator.Should().Contain("_store.CommitFillAsync(");
        coordinator.Should().NotContain("ITradeRepository");
        coordinator.Should().NotContain("TradeRecord");
        coordinator.Should().NotContain("EntityFramework");
        contract.Should().Contain("public interface ILivePositionExecutionStore");
        contract.Should().Contain("PositionExecutionTrade");
        store.Should().Contain("IDbContextFactory<AppDbContext>");
        store.Should().Contain("BeginTransactionAsync(");
        tradingPorts.Should().Contain("ITradeHistoryStore");
        tradingPorts.Should().Contain("IOpenPositionStore");
        tradingPorts.Should().Contain("ITradeRecommendationStore");
        tradingPorts.Should().Contain("IPatternSignalStore");
        File.Exists(broadRepositoryPath).Should().BeFalse();
        File.Exists(broadContractPath).Should().BeFalse();
        foreach (var purposeBuiltStore in purposeBuiltStores)
        {
            File.ReadAllText(Path.Combine(repository, "Data/Repositories", purposeBuiltStore))
                .Should().Contain("IDbContextFactory<AppDbContext>");
        }
    }

    [Fact]
    public void AutomaticAndManualExitPathsUseTheSameSubmissionCoordinator()
    {
        var repository = FindRepositoryRoot();
        var orders = File.ReadAllText(Path.Combine(repository, "Api/OrderEndpoints.cs"));
        var management = File.ReadAllText(Path.Combine(
            repository, "Services/Order/LiveOrderManagement.cs"));
        var managementContract = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/LiveOrderManagementContracts.cs"));
        var legacyOrderService = File.ReadAllText(Path.Combine(
            repository, "Services/Order/OrderService.cs"));
        var legacyOrderContract = File.ReadAllText(Path.Combine(
            repository, "Services/Order/IOrderService.cs"));
        var portfolio = File.ReadAllText(Path.Combine(repository, "desktop-app/src/pages/Portfolio.svelte"));
        var desktopEndpoints = File.ReadAllText(Path.Combine(repository, "desktop-app/src/api/endpoints.ts"));

        orders.Should().Contain("ILiveOrderManagement orders");
        orders.Should().Contain("orders.ClosePositionAsync(");
        orders.Should().Contain(".Produces<LiveOrderResponse>()");
        orders.Should().NotContain("IAccountManager");
        orders.Should().NotContain("IOpenPositionStore");
        orders.Should().NotContain("ILivePositionExecutionCoordinator");
        orders.Should().NotContain("ILiveEntryExecutionStore");
        File.ReadAllLines(Path.Combine(repository, "Api/OrderEndpoints.cs"))
            .Length.Should().BeLessThanOrEqualTo(110);
        management.Should().Contain("positionExecutions.SubmitFullExitAsync(");
        management.Should().Contain("GetBrokerContextForPositionExitAsync(position.AccountId");
        management.Should().Contain("GetBrokerContextForReconciliationAsync(position.AccountId");
        managementContract.Should().Contain("public interface ILiveOrderManagement");
        managementContract.Should().NotContain("Microsoft.AspNetCore");
        managementContract.Should().NotContain("StockTrader.Services");
        managementContract.Should().NotContain("StockTrader.Models");
        legacyOrderContract.Should().NotContain("CancelOrderAsync(",
            "an order identifier alone cannot identify its owning broker account");
        legacyOrderService.Should().NotContain("GetActiveBrokerServiceAsync(",
            "order lifecycle operations must never route through whichever account is active");
        var exitContextConsumers = Directory
            .GetFiles(repository, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "GetBrokerContextForPositionExitAsync(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        exitContextConsumers.Should().Equal(
            "AccountManager.cs",
            "IAccountManager.cs",
            "LiveOrderManagement.cs",
            "LivePositionMonitoringCycle.cs");
        portfolio.Should().Contain("orderApi.closePosition(symbol)");
        desktopEndpoints.Should().Contain("api.post('/api/orders/close-position'");
        orders.Should().NotContain("broker.ClosePositionAsync(");
    }

    [Fact]
    public void StockAnalysisDelegatesRecommendationMathAndUsesExplicitOperationsSettings()
    {
        var repository = FindRepositoryRoot();
        var servicePath = Path.Combine(repository, "Services/Analysis/StockAnalysisService.cs");
        var service = File.ReadAllText(servicePath);
        var endpoint = File.ReadAllText(Path.Combine(repository, "Api/AnalysisEndpoints.cs"));
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/StockAnalysisContracts.cs"));
        var desktopEndpoints = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/endpoints.ts"));
        var page = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Recommendations.svelte"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Analysis/StockRecommendationPolicy.cs"));

        File.ReadAllLines(servicePath).Length.Should().BeLessThanOrEqualTo(450);
        service.Should().Contain("StockRecommendationPolicy.Evaluate(");
        service.Should().Contain("IOptions<StockAnalysisSettings>");
        service.Should().Contain("_timeProvider.GetUtcNow()");
        service.Should().NotContain("DateTime.UtcNow");
        service.Should().NotContain("ComputeUpsideProbability(");
        service.Should().NotContain("ComputeRecommendedStopLoss(");
        service.Should().NotContain("ComputeRecommendedTarget(");
        endpoint.Should().Contain("MarketSymbolPolicy.Normalize(symbol)");
        endpoint.Should().Contain("StockAnalysisResponse.Create(analysis)");
        endpoint.Should().Contain("Produces<StockAnalysisResponse>");
        endpoint.Should().NotContain("Results.Ok(new");
        contracts.Should().Contain("record StockAnalysisResponse");
        contracts.Should().Contain("PatternCatalog.DisplayName(value.PatternType)");
        desktopEndpoints.Should().Contain("api.get<StockAnalysisResponse>");
        page.Should().Contain("analysis.currentPrice");
        page.Should().Contain("pattern.patternName");
        page.Should().Contain("formatFractionPercent(pattern.historicalWinRate)");
        page.Should().NotContain("analysis.CurrentPrice");
        page.Should().NotContain("analysis.Indicators");
        page.Should().NotContain("analysis.ActivePatterns");
        policy.Should().NotContain("StockTrader.Services");
        policy.Should().NotContain("DateTime.UtcNow");
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

    [Fact]
    public void BacktestExecutionChoicesHaveOneDomainCatalogOwner()
    {
        var repository = FindRepositoryRoot();
        var catalog = File.ReadAllText(Path.Combine(
            repository, "Domain/Backtesting/BacktestExecutionCatalog.cs"));
        var requestModel = File.ReadAllText(Path.Combine(repository, "Models/BacktestResult.cs"));
        var metadata = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/StrategyBuilderMetadataResponse.cs"));
        var backtestPage = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Backtest.svelte"));
        var backtestWorkspace = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/backtest/backtestWorkspace.js"));
        var optimizationPage = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Optimization.svelte"));
        var optimizationForm = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/OptimizationJobForm.svelte"));

        catalog.Should().Contain("public enum SlippageModel");
        catalog.Should().Contain("DefaultSlippageModel = SlippageModel.Adaptive");
        requestModel.Should().Contain("BacktestExecutionCatalog.DefaultSlippageModel");
        requestModel.Should().NotContain("public enum SlippageModel");
        metadata.Should().Contain("SlippageModels: BacktestExecutionCatalog.SlippageModels");
        backtestPage.Should().Contain("projectBacktestMetadata(metadata)");
        backtestPage.Should().NotContain("['Adaptive'");
        backtestWorkspace.Should().Contain("metadata?.slippageModels");
        backtestWorkspace.Should().NotContain("slippageModel: 'Adaptive'");
        optimizationPage.Should().NotContain("sizingModeOptions: ['FixedRisk'");
        optimizationPage.Should().NotContain("entryLogicOptions: ['AND'");
        optimizationForm.Should().Contain("{#each sizingModeOptions as [value, label]}");
        optimizationForm.Should().NotContain("[['FixedRisk'");
        optimizationForm.Should().NotContain("[['CurrentClose'");
    }

    [Fact]
    public void OptimizationRankingHasOneCatalogAndOneOrderingPolicy()
    {
        var repository = FindRepositoryRoot();
        var catalog = File.ReadAllText(Path.Combine(
            repository, "Domain/Optimization/OptimizationRankingCatalog.cs"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationRankingPolicy.cs"));
        var ranker = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationResultRanker.cs"));
        var promotion = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationAutoTuneService.cs"));
        var metadata = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/StrategyBuilderMetadataResponse.cs"));
        var page = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Optimization.svelte"));
        var frontendModel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/optimization/optimizationModel.js"));

        catalog.Should().Contain("public static class OptimizationRankingCatalog");
        catalog.Should().Contain("public const string DefaultCode = SortinoRatioCode");
        policy.Should().Contain("OptimizationRankingCatalog.MetricFor(rankBy)");
        ranker.Should().Contain("OptimizationRankingPolicy.OrderDescending(");
        promotion.Should().Contain("OptimizationRankingPolicy.OrderDescending(");
        ranker.Should().NotContain("ToLowerInvariant() switch");
        promotion.Should().NotContain("rankBy.ToLowerInvariant() switch");
        var management = File.ReadAllText(Path.Combine(
            repository, "Application/Optimization/OptimizationJobManagementService.cs"));
        management.Should().Contain("OptimizationRankingCatalog.Normalize(command.RankBy)");
        metadata.Should().Contain("OptimizationRankings: OptimizationRankingCatalog.All");
        page.Should().Contain("projectOptimizationRankingMetadata(metadata)");
        page.Should().NotContain("['sortinoRatio', '소르티노 비율']");
        frontendModel.Should().Contain("metadata?.optimizationRankings");
    }

    [Fact]
    public void TradingAccountsHaveExplicitApplicationPersistenceAndBrokerBoundaries()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/AccountEndpoints.cs"));
        var manager = File.ReadAllText(Path.Combine(
            repository, "Services/Account/AccountManager.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/TradingAccountStore.cs"));
        var factory = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/AccountBrokerServiceFactory.cs"));
        var brokerCatalog = File.ReadAllText(Path.Combine(
            repository, "Domain/Trading/BrokerCatalog.cs"));
        var accountPage = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Accounts.svelte"));

        endpoints.Should().Contain("TradingAccountCreateRequest");
        endpoints.Should().Contain("TradingAccountResponse");
        endpoints.Should().Contain("TradingAccountMetadataResponse");
        endpoints.Should().NotContain("StockTrader.Models");
        endpoints.Should().NotContain("DateTime.UtcNow");
        manager.Should().Contain("ITradingAccountStore");
        manager.Should().Contain("IAccountBrokerServiceFactory");
        manager.Should().Contain("TimeProvider");
        manager.Should().NotContain("AppDbContext");
        manager.Should().NotContain("DateTime.UtcNow");
        manager.Should().NotContain("new AlpacaBrokerService");
        store.Should().Contain("IDbContextFactory<AppDbContext>");
        factory.Should().Contain("BrokerType.Alpaca");
        factory.Should().Contain("BrokerType.LsSecurities");
        brokerCatalog.Should().Contain("public enum BrokerType");
        brokerCatalog.Should().Contain("CanSubmitProtectedEntry");
        brokerCatalog.Should().Contain("CanClosePartialPosition");
        File.Exists(Path.Combine(
            repository, "Services/Broker/BrokerServiceFactory.cs")).Should().BeFalse();
        accountPage.Should().Contain("accountApi.metadata()");
        accountPage.Should().Contain("brokerCapabilityLabels");
        accountPage.Should().Contain("row.accountName");
        accountPage.Should().NotContain("row.AccountName");
    }

    [Fact]
    public void RiskOverviewHasAThinApiAndDeterministicApplicationProjection()
    {
        var repository = FindRepositoryRoot();
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/RiskEndpoints.cs"));
        var queryContract = File.ReadAllText(Path.Combine(
            repository, "Application/Risk/RiskOverviewContracts.cs"));
        var projection = File.ReadAllText(Path.Combine(
            repository, "Application/Risk/PositionRiskProjectionPolicy.cs"));
        var query = File.ReadAllText(Path.Combine(
            repository, "Services/Risk/RiskOverviewQuery.cs"));
        var riskService = File.ReadAllText(Path.Combine(
            repository, "Services/Risk/MultiAccountRiskService.cs"));
        var worker = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/RiskMonitorService.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));

        endpoints.Should().Contain("IRiskOverviewQuery");
        endpoints.Should().Contain("Produces<RiskOverviewResponse>");
        endpoints.Should().NotContain("IOpenPositionStore");
        endpoints.Should().NotContain("ISettingsRepository");
        endpoints.Should().NotContain("DateTime.UtcNow");
        File.ReadAllLines(Path.Combine(repository, "Api/RiskEndpoints.cs")).Length
            .Should().BeLessThanOrEqualTo(20);

        queryContract.Should().Contain("interface IRiskOverviewQuery");
        queryContract.Should().NotContain("StockTrader.Models");
        queryContract.Should().NotContain("EntityFrameworkCore");
        projection.Should().NotContain("DateTime.UtcNow");
        query.Should().Contain("IOpenPositionQuery");
        query.Should().NotContain("IOpenPositionStore");
        query.Should().Contain("PositionRiskProjectionPolicy.Evaluate");
        query.Should().NotContain("DateTime.UtcNow");
        riskService.Should().Contain("legacyPositionAccountId");
        riskService.Should().Contain("TimeProvider");
        riskService.Should().NotContain("DateTime.UtcNow");
        worker.Should().Contain("RiskAlertPolicy.IsDue");
        worker.Should().NotContain("DateTime.UtcNow");
        registrations.Should().Contain("AddScoped<IRiskOverviewQuery, RiskOverviewQuery>");
        registrations.Should().Contain("RiskMonitorMaxConsecutiveFailures");
        registrations.Should().Contain("ValidateOnStart()");
    }

    [Fact]
    public void PortfolioPerformanceHasAThinApiAndDeterministicApplicationPolicy()
    {
        var repository = FindRepositoryRoot();
        var endpointsPath = Path.Combine(repository, "Api/PortfolioEndpoints.cs");
        var endpoints = File.ReadAllText(endpointsPath);
        var contract = File.ReadAllText(Path.Combine(
            repository, "Application/Portfolio/PortfolioPerformanceContracts.cs"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Portfolio/PortfolioPerformancePolicy.cs"));
        var query = File.ReadAllText(Path.Combine(
            repository, "Services/Portfolio/PortfolioPerformanceQuery.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));

        endpoints.Should().Contain("IPortfolioPerformanceQuery");
        endpoints.Should().Contain("Produces<PortfolioPerformanceResponse>");
        endpoints.Should().NotContain("IPatternStatsRepository");
        endpoints.Should().NotContain("Average(t => t.PnLPercent)");
        endpoints.Should().NotContain("var maxDrawdown");
        File.ReadAllLines(endpointsPath).Length.Should().BeLessThanOrEqualTo(45);

        contract.Should().Contain("interface IPortfolioPerformanceQuery");
        contract.Should().NotContain("StockTrader.Models");
        contract.Should().NotContain("EntityFrameworkCore");
        policy.Should().Contain("initialAccountEquity");
        policy.Should().Contain("ThenBy(trade => trade.Id)");
        policy.Should().NotContain("DateTime.UtcNow");
        query.Should().Contain("take: int.MaxValue");
        query.Should().Contain("PortfolioPerformancePolicy.Evaluate(");
        query.Should().NotContain("Task.WhenAll");
        registrations.Should().Contain(
            "AddScoped<IPortfolioPerformanceQuery, PortfolioPerformanceQuery>");
        registrations.Should().Contain("AddScoped<IOpenPositionQuery, OpenPositionQuery>");
    }

    [Fact]
    public void SignalAndPatternStatisticsReadsUseApplicationQueriesAndOneMetricPolicy()
    {
        var repository = FindRepositoryRoot();
        var signalEndpointPath = Path.Combine(repository, "Api/SignalEndpoints.cs");
        var statsEndpointPath = Path.Combine(repository, "Api/PatternStatsEndpoints.cs");
        var signalEndpoint = File.ReadAllText(signalEndpointPath);
        var statsEndpoint = File.ReadAllText(statsEndpointPath);
        var signalPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Signals/SignalListPolicy.cs"));
        var statisticsSelectionPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Statistics/PatternStatisticsSelectionPolicy.cs"));
        var statisticsContract = File.ReadAllText(Path.Combine(
            repository, "Application/Statistics/PatternStatisticsContracts.cs"));
        var metricPolicy = File.ReadAllText(Path.Combine(
            repository, "Domain/Statistics/PatternStatisticsMetricPolicy.cs"));
        var model = File.ReadAllText(Path.Combine(repository, "Models/PatternStats.cs"));
        var portfolioQuery = File.ReadAllText(Path.Combine(
            repository, "Services/Portfolio/PortfolioPerformanceQuery.cs"));
        var patternDetection = File.ReadAllText(Path.Combine(
            repository, "Services/Patterns/PatternDetectionService.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));
        var statsPage = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/PatternStats.svelte"));

        signalEndpoint.Should().Contain("ISignalListQuery");
        signalEndpoint.Should().Contain("Produces<SignalListResponse>");
        signalEndpoint.Should().NotContain("IPatternStatsRepository");
        signalEndpoint.Should().NotContain("ToDictionary");
        signalEndpoint.Should().NotContain("RiskReward");
        File.ReadAllLines(signalEndpointPath).Length.Should().BeLessThanOrEqualTo(30);
        statsEndpoint.Should().Contain("IPatternStatisticsQuery");
        statsEndpoint.Should().Contain("Produces<PatternStatisticsListResponse>");
        statsEndpoint.Should().NotContain("IPatternStatsRepository");
        File.ReadAllLines(statsEndpointPath).Length.Should().BeLessThanOrEqualTo(30);

        signalPolicy.Should().Contain("PatternStatisticsSelectionPolicy.Resolve(");
        signalPolicy.Should().NotContain("StockTrader.Models");
        statisticsSelectionPolicy.Should().Contain("StringComparison.OrdinalIgnoreCase");
        statisticsSelectionPolicy.Should().Contain("string.IsNullOrWhiteSpace(statistic.Symbol)");
        statisticsContract.Should().Contain("interface IPatternStatisticsQuery");
        statisticsContract.Should().NotContain("StockTrader.Models");
        metricPolicy.Should().Contain("CalculateExpectancy(");
        metricPolicy.Should().Contain("CalculateProfitFactor(");
        model.Should().Contain("PatternStatisticsMetricPolicy.CalculateExpectancy(");
        model.Should().Contain("PatternStatisticsMetricPolicy.CalculateProfitFactor(");
        portfolioQuery.Should().Contain("IPatternStatisticsQuery");
        portfolioQuery.Should().NotContain("IPatternStatsRepository");
        patternDetection.Should().Contain("IPatternStatisticsQuery");
        patternDetection.Should().Contain("PatternStatisticsSelectionPolicy.Resolve(");
        patternDetection.Should().NotContain("IPatternStatsRepository");
        registrations.Should().Contain("AddScoped<IPatternStatisticsQuery, PatternStatisticsQuery>");
        registrations.Should().Contain("AddScoped<ISignalListQuery, SignalListQuery>");

        statsPage.Should().Contain("data?.stats");
        statsPage.Should().NotContain("data?.Stats");
        statsPage.Should().NotContain("row.Expectancy");
        statsPage.Should().NotContain("row.Pattern");
    }

    [Fact]
    public void DailyReportWorkerIsAThinAdapterOverDeterministicApplicationPorts()
    {
        var repository = FindRepositoryRoot();
        var workerPath = Path.Combine(
            repository, "BackgroundServices/DailyReportService.cs");
        var worker = File.ReadAllText(workerPath);
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Application/Reporting/DailyReportContracts.cs"));
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Reporting/DailyReportPolicy.cs"));
        var generator = File.ReadAllText(Path.Combine(
            repository, "Application/Reporting/DailyReportGenerator.cs"));
        var activityStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/DailyReportActivityStore.cs"));
        var services = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));
        var dataServices = File.ReadAllText(Path.Combine(
            repository, "Extensions/DataServiceExtensions.cs"));
        var notifications = File.ReadAllText(Path.Combine(
            repository, "Extensions/NotificationServiceExtensions.cs"));

        File.ReadAllLines(workerPath).Length.Should().BeLessThanOrEqualTo(120);
        worker.Should().Contain("IDailyReportScheduleQuery");
        worker.Should().Contain("IDailyReportGenerator");
        worker.Should().Contain("DailyReportPolicy.CalculateDelay(");
        worker.Should().NotContain("ISettingsRepository");
        worker.Should().NotContain("ITradeHistoryStore");
        worker.Should().NotContain("ITradeRecommendationStore");
        worker.Should().NotContain("IAccountManager");
        worker.Should().NotContain("INotificationDispatcher");
        worker.Should().NotContain("DateTime.UtcNow");
        worker.Should().NotContain(".Sum(");
        worker.Should().NotContain("new DailyReportData");

        contracts.Should().Contain("interface IDailyReportActivityStore");
        contracts.Should().Contain("interface IActiveAccountEquityReader");
        contracts.Should().Contain("interface IDailyReportPublisher");
        contracts.Should().NotContain("StockTrader.Models");
        contracts.Should().NotContain("StockTrader.Data");
        contracts.Should().NotContain("StockTrader.Services");
        policy.Should().Contain("TimeZoneInfo.ConvertTimeToUtc(localStart");
        policy.Should().Contain("TimeZoneInfo.ConvertTimeToUtc(localEnd");
        policy.Should().NotContain("DateTime.UtcNow");
        generator.Should().Contain("timeProvider.GetUtcNow()");
        generator.Should().Contain("Task.WhenAll(activityTask, equityTask)");
        activityStore.Should().Contain("trade.ExitTime >= fromUtc");
        activityStore.Should().Contain("trade.ExitTime < toUtc");
        activityStore.Should().NotContain("Take(50)");
        services.Should().Contain("AddScoped<IDailyReportGenerator, DailyReportGenerator>");
        services.Should().Contain("DailyReportRetryDelayMinutes must be positive");
        dataServices.Should().Contain(
            "AddSingleton<IDailyReportActivityStore, DailyReportActivityStore>");
        notifications.Should().Contain("AddSingleton<IDailyReportPublisher>");
    }

    [Fact]
    public void DashboardUsesOneExplicitReadModelWithoutFabricatedRiskMetrics()
    {
        var repository = FindRepositoryRoot();
        var endpointPath = Path.Combine(repository, "Api/DashboardEndpoints.cs");
        var endpoint = File.ReadAllText(endpointPath);
        var contract = File.ReadAllText(Path.Combine(
            repository, "Application/Dashboard/DashboardContracts.cs"));
        var response = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/DashboardContracts.cs"));
        var query = File.ReadAllText(Path.Combine(
            repository, "Services/Dashboard/DashboardQuery.cs"));
        var activityStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/DashboardActivityStore.cs"));
        var riskContract = File.ReadAllText(Path.Combine(
            repository, "Application/Risk/RiskOverviewContracts.cs"));
        var legacyTypes = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/types.ts"));

        File.ReadAllLines(endpointPath).Length.Should().BeLessThanOrEqualTo(25);
        endpoint.Should().Contain("IDashboardQuery");
        endpoint.Should().Contain("Produces<DashboardResponse>");
        endpoint.Should().NotContain("StockTrader.Data");
        endpoint.Should().NotContain("StockTrader.Services");
        endpoint.Should().NotContain("ISettingsRepository");
        endpoint.Should().NotContain("IAccountManager");
        endpoint.Should().NotContain("Task.WhenAll");
        contract.Should().Contain("interface IDashboardQuery");
        contract.Should().Contain("interface IDashboardActivityStore");
        contract.Should().NotContain("StockTrader.Models");
        response.Should().Contain("record DashboardResponse");
        response.Should().Contain("OpenPositionResponseMapper.Map");
        query.Should().Contain("IRiskOverviewQuery");
        query.Should().Contain("IActiveBrokerAccountQuery");
        activityStore.Should().Contain("CountAsync(signal => signal.IsActive");
        activityStore.Should().Contain("ThenByDescending(recommendation => recommendation.Id)");
        riskContract.Should().Contain("OpenPositionListSnapshot OpenPositions");
        riskContract.Should().Contain("OrderMode OrderMode");
        legacyTypes.Should().NotContain("interface DashboardData");
    }

    [Fact]
    public void TradeActivityEndpointsAreTypedAdaptersOverOneApplicationQuery()
    {
        var repository = FindRepositoryRoot();
        var endpointPath = Path.Combine(repository, "Api/TradeEndpoints.cs");
        var endpoint = File.ReadAllText(endpointPath);
        var contracts = File.ReadAllText(Path.Combine(
            repository, "Api/Contracts/TradeActivityContracts.cs"));
        var query = File.ReadAllText(Path.Combine(
            repository, "Application/Trading/TradeActivityQuery.cs"));
        var store = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/TradeActivityStore.cs"));
        var desktopEndpoints = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/endpoints.ts"));
        var recommendations = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/Recommendations.svelte"));
        var history = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/pages/History.svelte"));
        var desktopModel = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/features/trades/tradeActivityModel.js"));

        File.ReadAllLines(endpointPath).Length.Should().BeLessThanOrEqualTo(80);
        endpoint.Should().Contain("ITradeActivityQuery query");
        endpoint.Should().Contain("Produces<TradeRecommendationListResponse>");
        endpoint.Should().Contain("Produces<TradeHistoryResponse>");
        endpoint.Should().Contain("string? pattern");
        endpoint.Should().Contain("string? from");
        endpoint.Should().Contain("string? to");
        endpoint.Should().NotContain("PatternType? pattern");
        endpoint.Should().NotContain("StockTrader.Data");
        endpoint.Should().NotContain("StockTrader.Models");
        endpoint.Should().NotContain("ITradeHistoryStore");
        endpoint.Should().NotContain("ITradeRecommendationStore");
        endpoint.Should().NotContain("LiveEntryOrderStatusPolicy");
        endpoint.Should().NotContain("RiskRewardRatio");
        endpoint.Should().NotContain("HoldingDays");
        contracts.Should().Contain("record TradeRecommendationListResponse");
        contracts.Should().Contain("record TradeHistoryResponse");
        query.Should().Contain("interface ITradeActivityStore");
        query.Should().Contain("interface ITradeActivityQuery");
        query.Should().Contain("TradeActivityQueryPolicy.ParsePattern(query.Pattern");
        query.Should().Contain("TradeActivityQueryPolicy.ParseUtc(query.From, \"시작일\"");
        query.Should().Contain("TradeActivityQueryPolicy.ParseUtc(query.To, \"종료일\"");
        query.Should().NotContain("StockTrader.Data");
        query.Should().NotContain("StockTrader.Models");
        store.Should().Contain("IDbContextFactory<AppDbContext>");
        store.Should().Contain("ThenByDescending(row => row.Id)");
        desktopEndpoints.Should().Contain(
            "api.get<TradeRecommendationListResponse>('/api/trades/recommendations'");
        desktopEndpoints.Should().Contain(
            "api.get<TradeHistoryResponse>('/api/trades/history'");
        recommendations.Should().NotContain("data?.Recommendations");
        recommendations.Should().NotContain("row.EntryStatus");
        history.Should().NotContain("data?.Trades");
        history.Should().NotContain("row.PnL");
        history.Should().Contain("tradeApiError(e,");
        desktopModel.Should().Contain("Array.isArray(response?.errors)");
    }

    [Fact]
    public void SupersededLegacyActivityIsPreservedAndExcludedByEveryOperationalReader()
    {
        var repository = FindRepositoryRoot();
        var migrationPath = Directory.GetFiles(
                Path.Combine(repository, "Data/EfMigrations"),
                "*_SupersedeLegacyActivityDuplicates.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var migration = File.ReadAllText(migrationPath);
        var readers = new[]
        {
            "Data/Repositories/PatternSignalStore.cs",
            "Data/Repositories/ManualOrderSignalStore.cs",
            "Data/Repositories/DashboardActivityStore.cs",
            "Data/Repositories/DailyReportActivityStore.cs",
            "Data/Repositories/TradeActivityStore.cs",
            "Data/Repositories/TradeRecommendationStore.cs",
            "Data/Repositories/LiveEntryExecutionStore.cs",
            "Data/Repositories/LiveSignalEvaluationStore.cs",
        };

        migration.Should().Contain("SET \"IsSuperseded\" = 1");
        migration.Should().Contain("\"SignalBarAt\" IS NULL");
        migration.Should().Contain("\"SourceSignalId\" IS NULL");
        migration.Should().Contain("\"WasExecuted\" = 0");
        migration.Should().Contain("\"EntryRequestedAt\" IS NULL");
        migration.Should().NotContain("DELETE FROM");
        foreach (var path in readers)
        {
            File.ReadAllText(Path.Combine(repository, path))
                .Should().Contain("IsSuperseded", $"{path} is an operational activity reader");
        }
    }

    [Fact]
    public void SignalActionabilityHasOneClockedPolicyAndNoManualAgeConstant()
    {
        var repository = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(
            repository, "Application/Signals/SignalFreshnessPolicy.cs"));
        var signalQuery = File.ReadAllText(Path.Combine(
            repository, "Services/Signal/SignalListQuery.cs"));
        var dashboard = File.ReadAllText(Path.Combine(
            repository, "Services/Dashboard/DashboardQuery.cs"));
        var manualOrder = File.ReadAllText(Path.Combine(
            repository, "Services/Order/ManualOrderWorkflow.cs"));
        var manualPolicy = File.ReadAllText(Path.Combine(
            repository, "Application/Execution/ManualSignalEntryPolicy.cs"));
        var signalStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/PatternSignalStore.cs"));
        var dashboardStore = File.ReadAllText(Path.Combine(
            repository, "Data/Repositories/DashboardActivityStore.cs"));
        var settings = File.ReadAllText(Path.Combine(repository, "appsettings.json"));

        policy.Should().Contain("SignalFreshnessStatus.FutureDated");
        policy.Should().Contain("SignalFreshnessWindow");
        signalQuery.Should().Contain("freshness.GetWindow(observedAtUtc)");
        signalQuery.Should().Contain("timeProvider.GetUtcNow()");
        dashboard.Should().Contain("signalFreshness.GetWindow(");
        dashboard.Should().Contain("timeProvider.GetUtcNow()");
        manualOrder.Should().Contain("_entryPolicy.EvaluateSignal(");
        manualOrder.Should().NotContain("MaxSignalAge");
        manualOrder.Should().NotContain("TimeSpan.FromHours(24)");
        manualPolicy.Should().Contain("freshness.Evaluate(");
        manualPolicy.Should().Contain("SignalFreshnessStatus.FutureDated");
        signalStore.Should().Contain("detectedFromInclusiveUtc");
        signalStore.Should().Contain("detectedThroughInclusiveUtc");
        dashboardStore.Should().Contain("signalDetectedFromInclusiveUtc");
        dashboardStore.Should().Contain("signalDetectedThroughInclusiveUtc");
        settings.Should().Contain("\"SignalLifecycle\"");
        settings.Should().Contain("\"ActionableLifetimeHours\": 24");
    }

    [Fact]
    public void DesktopHasNoUnreachableLegacyOperationalPagesOrApiWrappers()
    {
        var repository = FindRepositoryRoot();
        var retiredPages = new[]
        {
            "Dashboard.svelte",
            "Signals.svelte",
            "Risk.svelte",
            "Ml.svelte",
        };
        var pages = Path.Combine(repository, "desktop-app/src/pages");
        var app = File.ReadAllText(Path.Combine(repository, "desktop-app/src/App.svelte"));
        var endpoints = File.ReadAllText(Path.Combine(
            repository, "desktop-app/src/api/endpoints.ts"));

        foreach (var page in retiredPages)
            File.Exists(Path.Combine(pages, page)).Should().BeFalse();
        app.Should().NotContain("currentPage === 'ml'");
        endpoints.Should().NotContain("export const dashboardApi");
        endpoints.Should().NotContain("export const signalApi");
        endpoints.Should().NotContain("export const riskApi");
        endpoints.Should().NotContain("export const mlApi");
    }

    [Fact]
    public void AuthenticationPolicyIsClockedApplicationCodeBehindPurposeSpecificStores()
    {
        var repository = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            repository,
            "Application/Authentication/AuthenticationService.cs"));
        var userPort = File.ReadAllText(Path.Combine(
            repository,
            "Application/Authentication/IAuthenticationUserStore.cs"));
        var auditContracts = File.ReadAllText(Path.Combine(
            repository,
            "Application/Authentication/SecurityAuditContracts.cs"));
        var userStore = File.ReadAllText(Path.Combine(
            repository,
            "Data/Repositories/AuthenticationUserStore.cs"));
        var auditStore = File.ReadAllText(Path.Combine(
            repository,
            "Data/Repositories/SecurityAuditStore.cs"));
        var auditAdapter = File.ReadAllText(Path.Combine(
            repository,
            "Services/Auth/AuditService.cs"));
        var endpoints = File.ReadAllText(Path.Combine(repository, "Api/AuthEndpoints.cs"));

        service.Should().Contain("IAuthenticationUserStore users");
        service.Should().Contain("TimeProvider timeProvider");
        service.Should().Contain("timeProvider.GetUtcNow()");
        service.Should().NotContain("StockTrader.Data");
        service.Should().NotContain("StockTrader.Models");
        service.Should().NotContain("Microsoft.EntityFrameworkCore");
        service.Should().NotContain("DateTime.UtcNow");
        userPort.Should().NotContain("AppDbContext");
        userPort.Should().NotContain("AppUser");
        auditContracts.Should().Contain("interface ISecurityAuditStore");
        auditContracts.Should().Contain("interface ISecurityAuditSink");
        userStore.Should().Contain("IDbContextFactory<AppDbContext>");
        userStore.Should().Contain(": IAuthenticationUserStore");
        userStore.Should().Contain("RecordFailedLoginAsync(");
        userStore.Should().Contain("entity.LockedUntil <= observedAt");
        userStore.Should().Contain("ExecuteUpdateAsync(");
        auditStore.Should().Contain(": ISecurityAuditStore");
        auditAdapter.Should().Contain("ISecurityAuditStore _store");
        auditAdapter.Should().Contain("TimeProvider _timeProvider");
        auditAdapter.Should().NotContain("AppDbContext");
        auditAdapter.Should().NotContain("DateTime.UtcNow");
        endpoints.Should().Contain("IUserAuthenticationService auth");
        endpoints.Should().Contain("AuthenticationPolicy policy");
        endpoints.Should().NotContain("IOptionsMonitor<SecuritySettings>");
        endpoints.Should().NotContain("StockTrader.Data");
        File.Exists(Path.Combine(repository, "Services/Auth/AuthService.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void StatisticsAndMlSchedulingHaveExplicitClocksAndValidatedOperationalValues()
    {
        var repository = FindRepositoryRoot();
        var resultBuilder = File.ReadAllText(Path.Combine(
            repository,
            "Services/Backtest/BacktestResultBuilder.cs"));
        var performance = File.ReadAllText(Path.Combine(
            repository,
            "Services/Backtest/PerformanceCalculator.cs"));
        var statistics = File.ReadAllText(Path.Combine(
            repository,
            "Services/Statistics/StatisticsService.cs"));
        var statisticsStore = File.ReadAllText(Path.Combine(
            repository,
            "Data/Repositories/PatternStatsRepository.cs"));
        var statisticsPort = File.ReadAllText(Path.Combine(
            repository,
            "Data/Repositories/IPatternStatsRepository.cs"));
        var retraining = File.ReadAllText(Path.Combine(
            repository,
            "BackgroundServices/MLRetrainingService.cs"));
        var schedule = File.ReadAllText(Path.Combine(
            repository,
            "Application/Analysis/MlRetrainingSchedulePolicy.cs"));
        var settings = File.ReadAllText(Path.Combine(repository, "appsettings.json"));

        resultBuilder.Should().Contain("PerformanceCalculator.ComputePerPatternStats(");
        resultBuilder.Should().Contain("PerformanceCalculator.ComputePerStrategyStats(");
        resultBuilder.Should().Contain("input.To);");
        performance.Should().Contain("DateTime calculatedAt");
        performance.Should().NotContain("DateTime.UtcNow");
        statistics.Should().Contain("TimeProvider _timeProvider");
        statistics.Should().Contain("var observedAt = _timeProvider.GetUtcNow().UtcDateTime");
        statistics.Should().Contain("IOptions<PatternStatisticsSettings>");
        statistics.Should().NotContain("DateTime.UtcNow");
        statisticsStore.Should().Contain("row.LastUpdated = stats.LastUpdated");
        statisticsStore.Should().NotContain("DateTime.UtcNow");
        statisticsPort.Should().NotContain("SaveAsync(PatternStats");
        retraining.Should().Contain("TimeProvider timeProvider");
        retraining.Should().Contain("MlRetrainingSchedulePolicy.CalculateRecurringDelay(");
        retraining.Should().Contain("Task.Delay(");
        retraining.Should().NotContain("DateTime.UtcNow");
        retraining.Should().NotContain("PeriodicTimer");
        schedule.Should().Contain("CalculateInitialDelay(");
        schedule.Should().Contain("CalculateRecurringDelay(");
        schedule.Should().NotContain("DateTime.UtcNow");
        settings.Should().Contain("\"AutoRetrainAfterEt\": \"17:00\"");
        settings.Should().Contain("\"AutoRetrainMaxConsecutiveFailures\": 5");
        settings.Should().Contain("\"AutoRetrainCooldownMinutes\": 5");
        settings.Should().Contain("\"AutoRetrainMaxRetries\": 3");
        settings.Should().Contain("\"PatternStatistics\"");
        settings.Should().Contain("\"CacheMinutes\": 5");
    }

    [Fact]
    public void BacktestPeriodMetricsHaveOneUnitExplicitPolicy()
    {
        var repository = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(
            repository,
            "Application/Backtesting/BacktestPerformancePolicy.cs"));
        var resultBuilder = File.ReadAllText(Path.Combine(
            repository,
            "Services/Backtest/BacktestResultBuilder.cs"));
        var performance = File.ReadAllText(Path.Combine(
            repository,
            "Services/Backtest/PerformanceCalculator.cs"));

        policy.Should().Contain("totalReturnFraction");
        policy.Should().Contain("maxDrawdownFraction");
        policy.Should().Contain("evaluationFrom");
        policy.Should().Contain("evaluationTo");
        policy.Should().NotContain("StockTrader.Models");
        policy.Should().NotContain("StockTrader.Services");
        resultBuilder.Should().Contain("BacktestPerformancePolicy.Evaluate(");
        resultBuilder.Should().Contain("input.From");
        resultBuilder.Should().Contain("input.To");
        resultBuilder.Should().NotContain("tradeCycles.Max(t => t.ExitTime)");
        resultBuilder.Should().NotContain("tradeCycles.Min(t => t.EntryTime)");
        performance.Should().NotContain("ComputeAnnualizedReturn");
        performance.Should().NotContain("ComputeCalmarRatio");
        performance.Should().NotContain("ComputeSharpeRatio");
        performance.Should().NotContain("ComputeSortinoRatio");
    }

    [Fact]
    public void BrokerSnapshotsAndStreamingStatusDoNotInventTradingEventTime()
    {
        var repository = FindRepositoryRoot();
        var brokerPort = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/IBrokerService.cs"));
        var brokerSnapshot = File.ReadAllText(Path.Combine(
            repository, "Application/Accounts/BrokerPositionSnapshot.cs"));
        var brokerDirectory = Path.Combine(repository, "Services/Broker");
        var brokerAdapters = string.Join(
            Environment.NewLine,
            Directory.GetFiles(brokerDirectory, "*.cs")
                .Select(File.ReadAllText));
        var factory = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/AccountBrokerServiceFactory.cs"));
        var lsTimestampParser = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/LsOrderTimestampParser.cs"));
        var lsBroker = File.ReadAllText(Path.Combine(
            repository, "Services/Broker/LsSecuritiesBrokerService.cs"));
        var normalizedLsBroker = lsBroker.ReplaceLineEndings("\n");
        var lsAuth = File.ReadAllText(Path.Combine(
            repository, "Services/LsSecurities/LsAuthService.cs"));
        var lsTiming = File.ReadAllText(Path.Combine(
            repository, "Services/LsSecurities/LsOperationalTimingPolicy.cs"));
        var lsDataFeed = File.ReadAllText(Path.Combine(
            repository, "Services/DataFeed/LsSecuritiesDataFeedService.cs"));
        var streamingStatus = File.ReadAllText(Path.Combine(
            repository, "Services/Streaming/StreamingStatusService.cs"));
        var streamingWorker = File.ReadAllText(Path.Combine(
            repository, "BackgroundServices/AlpacaStreamingService.cs"));
        var orderPort = File.ReadAllText(Path.Combine(
            repository, "Services/Order/IOrderService.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/ServiceCollectionExtensions.cs"));
        var settings = File.ReadAllText(Path.Combine(repository, "appsettings.json"));

        brokerPort.Should().Contain("IReadOnlyList<BrokerPositionSnapshot>");
        brokerPort.Should().NotContain("Task<List<Position>>");
        brokerSnapshot.Should().NotContain("OpenedAt");
        brokerSnapshot.Should().NotContain("StockTrader.Models");
        brokerAdapters.Should().NotContain("DateTime.UtcNow");
        brokerAdapters.Should().NotContain("new Position");
        File.Exists(Path.Combine(
            brokerDirectory, "AlpacaBrokerService.cs")).Should().BeFalse();
        factory.Should().Contain("TimeProvider timeProvider");
        factory.Should().Contain("new DynamicAlpacaBrokerService(");
        brokerAdapters.Should().Contain("LsOrderTimestampParser.TryParseUtc(");
        lsTimestampParser.Should().Contain("OrdTime");
        lsTimestampParser.Should().Contain("TimeZoneInfo.ConvertTimeToUtc(");
        lsTimestampParser.Should().NotContain("DateTime.UtcNow");
        normalizedLsBroker.Should().Contain(
            "\"/stock/accno\",\n                \"CSPAQ13700\"");
        lsAuth.Should().Contain("TimeProvider timeProvider");
        lsAuth.Should().Contain("LsOperationalTimingPolicy.CalculateTokenExpiryUtc(");
        lsAuth.Should().Contain("Task.Delay(delay, _timeProvider, ct)");
        lsAuth.Should().NotContain("DateTime.UtcNow");
        lsTiming.Should().Contain("CalculateRateLimitDelay(");
        lsTiming.Should().Contain("DailyTokenExpiryKst = new(7, 0)");
        lsTiming.Should().Contain("MinimumChartRequestInterval = TimeSpan.FromSeconds(1)");
        lsTiming.Should().NotContain("DateTime.UtcNow");
        lsDataFeed.Should().Contain("TimeProvider timeProvider");
        lsDataFeed.Should().NotContain("DateTime.UtcNow");
        orderPort.Should().NotContain("GetOpenPositionsAsync");
        streamingStatus.Should().Contain("TimeProvider timeProvider");
        streamingStatus.Should().Contain("IOptions<StreamingSettings>");
        streamingStatus.Should().NotContain("DateTime.UtcNow");
        streamingWorker.Should().Contain("IOptions<StreamingSettings>");
        streamingWorker.Should().Contain("Task.Delay(delay, _timeProvider");
        streamingWorker.Should().NotContain("DateTime.UtcNow");
        registrations.Should().Contain("AddOptions<StreamingSettings>()");
        registrations.Should().Contain("AddOptions<LsSecuritiesSettings>()");
        settings.Should().NotContain("\"StreamTypes\"");
        settings.Should().NotContain("\"DataBaseUrl\"");
        settings.Should().NotContain("\"BaseUrl\": \"https://paper-api.alpaca.markets\"");
        settings.Should().Contain("\"MaxReconnectAttempts\": 10");
        settings.Should().Contain("\"InitialReconnectDelaySeconds\": 2");
        settings.Should().Contain("\"MaxReconnectDelaySeconds\": 300");
        settings.Should().Contain("\"StatusStalenessSeconds\": 180");
        settings.Should().Contain("\"BarFlushIntervalSeconds\": 5");
        settings.Should().Contain("\"WatchlistSyncIntervalSeconds\": 60");
        settings.Should().Contain("\"BufferCapacity\": 10000");
        settings.Should().Contain("\"TokenExpirySafetyMinutes\": 5");
    }

    [Fact]
    public void PersistedEntitiesAndNotificationRenderingDoNotReadTheSystemClock()
    {
        var repository = FindRepositoryRoot();
        var entityPaths = new[]
        {
            "Models/CustomPatternDefinition.cs",
            "Models/FinancialImportRun.cs",
            "Models/FinancialSnapshot.cs",
            "Models/OptimizationJob.cs",
            "Models/OptimizationResult.cs",
            "Models/SymbolProfile.cs"
        };

        foreach (var path in entityPaths)
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            source.Should().NotContain("DateTime.UtcNow", $"{path} is a persistence shape");
            source.Should().NotContain("DateTime.Now", $"{path} is a persistence shape");
        }

        foreach (var path in new[]
                 {
                     "Services/Notification/DiscordNotificationChannel.cs",
                     "Services/Notification/EmailNotificationChannel.cs"
                 })
        {
            var source = File.ReadAllText(Path.Combine(repository, path));
            source.Should().Contain("TimeProvider", $"{path} renders observed time");
            source.Should().NotContain("DateTime.UtcNow");
            source.Should().NotContain("DateTime.Now");
        }

        var registrations = File.ReadAllText(Path.Combine(
            repository, "Extensions/NotificationServiceExtensions.cs"));
        registrations.Should().Contain("GetRequiredService<TimeProvider>()");
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

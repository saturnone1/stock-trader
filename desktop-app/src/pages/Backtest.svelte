<script>
  import { onMount } from 'svelte'
  import { RotateCcw } from 'lucide-svelte'
  import { backtestApi, financialFactorApi, metadataApi, patternApi } from '../api/endpoints'
  import FinancialFactorBuilder from '../lib/FinancialFactorBuilder.svelte'
  import UniverseBuilder from '../lib/UniverseBuilder.svelte'
  import BacktestFactorLabPanel from '../features/backtest/BacktestFactorLabPanel.svelte'
  import BacktestFactorRanking from '../features/backtest/BacktestFactorRanking.svelte'
  import BacktestExecutionInputs from '../features/backtest/BacktestExecutionInputs.svelte'
  import BacktestPerformanceBreakdown from '../features/backtest/BacktestPerformanceBreakdown.svelte'
  import BacktestPatternSelection from '../features/backtest/BacktestPatternSelection.svelte'
  import BacktestResultSummary from '../features/backtest/BacktestResultSummary.svelte'
  import BacktestRiskSettings from '../features/backtest/BacktestRiskSettings.svelte'
  import BacktestScenarioComparison from '../features/backtest/BacktestScenarioComparison.svelte'
  import BacktestTradeHistory from '../features/backtest/BacktestTradeHistory.svelte'
  import BacktestTimingOptions from '../features/backtest/BacktestTimingOptions.svelte'
  import BacktestUniverseComparison from '../features/backtest/BacktestUniverseComparison.svelte'
  import BacktestUniverseControls from '../features/backtest/BacktestUniverseControls.svelte'
  import BacktestValidationResults from '../features/backtest/BacktestValidationResults.svelte'
  import { runBacktestScenarios, runPlainBacktest } from '../features/backtest/backtestExecution'
  import { queryFactorLabCandidates } from '../features/backtest/backtestFactorLab'
  import { buildBacktestResearchPlans, buildBacktestViewModel } from '../features/backtest/backtestViewModel'
  import {
    createBacktestForm,
    createCustomFactorExperiment,
    createFactorLab,
    createTimingLab,
    createUniverseComparison,
    projectBacktestMetadata,
    toggleSelection
  } from '../features/backtest/backtestWorkspace'
  import {
    factorExperimentPresets,
    factorRankingOptions,
    timingStructureOptions,
    timingWindowOptions,
    uniqueSymbols
  } from '../features/backtest/backtestResearch'

  let timeFrameOptions = []
  let dataSourceOptions = [['', '기본 설정']]
  let dataProviders = []
  let slippageOptions = []
  let defaultSlippageModel = ''

  let patterns = []
  let loading = true
  let running = false
  let error = ''
  let result = null
  let selectedPatternIds = []
  let comparisonResults = []
  let activeScenarioKey = ''
  let runStatus = ''
  let universeBuilderSymbols = []
  let financialFactorSymbols = []
  let universeBuilderSummary = null
  let financialFactorSummary = null
  let financialFactorFilters = null
  let factorLabLoading = false
  let factorLabError = ''
  let factorLabSummaries = []
  let factorLabVariants = []
  let factorLabBaseSignature = ''

  let timingLab = createTimingLab()
  let universeComparison = createUniverseComparison()
  let factorLab = createFactorLab()
  let form = createBacktestForm()

  $: viewModel = buildBacktestViewModel({
    form, patterns, selectedPatternIds, timingLab, universeComparison,
    universeBuilderSymbols, financialFactorSymbols, financialFactorFilters,
    factorLab, factorLabVariants, factorLabBaseSignature, factorLabSummaries,
    comparisonResults, activeScenarioKey, dataProviders, timeFrameOptions
  })

  onMount(async () => {
    await Promise.all([loadMetadata(), loadPatterns()])
    loading = false
  })

  async function loadMetadata() {
    try {
      const metadata = await metadataApi.getStrategyBuilder()
      const projected = projectBacktestMetadata(metadata)
      timeFrameOptions = projected.timeFrameOptions
      dataProviders = projected.dataProviders
      dataSourceOptions = projected.dataSourceOptions
      slippageOptions = projected.slippageOptions
      defaultSlippageModel = projected.defaultSlippageModel
      if (!slippageOptions.some(([value]) => value === form.slippageModel)) {
        form.slippageModel = defaultSlippageModel
      }
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '시간축·데이터 공급자 정보를 불러오지 못했습니다.'
    }
  }

  async function loadPatterns() {
    try {
      const res = await patternApi.list()
      patterns = res.data ?? []
      if (!selectedPatternIds.length && patterns[0]) {
        selectedPatternIds = [String(patterns[0].id)]
      }
      error = ''
    } catch (e) {
      error = e?.message || '패턴 목록을 불러오지 못했습니다.'
    }
  }

  function togglePattern(id) {
    const value = String(id)
    selectedPatternIds = selectedPatternIds.includes(value)
      ? selectedPatternIds.filter((item) => item !== value)
      : [...selectedPatternIds, value]
  }

  function toggleTimingStructure(id) {
    timingLab.selectedStructures = toggleSelection(timingLab.selectedStructures, id)
  }

  function toggleTimingWindow(id) {
    timingLab.selectedWindows = toggleSelection(timingLab.selectedWindows, id)
  }

  function toggleFactorPreset(id) {
    factorLab.selectedPresets = toggleSelection(factorLab.selectedPresets, id)
  }

  function addCustomFactorExperiment() {
    factorLab.customExperiments = [
      ...factorLab.customExperiments,
      createCustomFactorExperiment(
        factorLab.customExperiments.length + 1,
        `custom-${Date.now()}-${factorLab.customExperiments.length + 1}`
      )
    ]
  }

  function removeCustomFactorExperiment(id) {
    factorLab.customExperiments = factorLab.customExperiments.filter((item) => item.id !== id)
  }

  async function loadFactorLabCandidates(baseSymbols, options = {}) {
    const { silent = false } = options
    const normalizedBaseSymbols = uniqueSymbols(baseSymbols)
    const baseSignature = normalizedBaseSymbols.join('|')
    const definitions = viewModel.factorDefinitions

    if (!factorLab.enabled || definitions.length === 0 || normalizedBaseSymbols.length === 0) {
      factorLabError = ''
      factorLabSummaries = []
      factorLabVariants = []
      factorLabBaseSignature = baseSignature
      return []
    }

    factorLabLoading = true
    try {
      const candidates = await queryFactorLabCandidates({
        definitions,
        baseSymbols: normalizedBaseSymbols,
        minMatchedSymbols: factorLab.minMatchedSymbols,
        query: (payload) => financialFactorApi.query(payload)
      })
      factorLabSummaries = candidates.summaries
      factorLabVariants = candidates.variants
      factorLabBaseSignature = baseSignature
      factorLabError = ''
      return factorLabVariants
    } catch (e) {
      factorLabError = e?.response?.data?.error || e?.message || '팩터 실험 후보를 불러오지 못했습니다.'
      factorLabSummaries = []
      factorLabVariants = []
      factorLabBaseSignature = baseSignature
      if (!silent) throw e
      return []
    } finally {
      factorLabLoading = false
    }
  }

  async function previewFactorLab() {
    try {
      await loadFactorLabCandidates(viewModel.symbols)
    } catch {
      // loadFactorLabCandidates already surfaces the error in factorLabError
    }
  }

  function selectScenarioResult(key) {
    const next = comparisonResults.find((item) => item.key === key)
    if (!next) return
    activeScenarioKey = key
    result = next.data
  }

  async function runBacktest() {
    const symbols = viewModel.symbols
    const customPatterns = viewModel.selectedPatterns
    if (!symbols.length || !form.from || !form.to || !customPatterns.length) {
      error = '종목, 기간, 패턴을 모두 입력하세요.'
      return
    }

    if (timingLab.enabled && !timingLab.includeBaseScenario && (!timingLab.selectedStructures.length || !timingLab.selectedWindows.length)) {
      error = '타이밍 연구실을 켰다면 기본 시나리오를 포함하거나 비교 구조와 기간을 하나 이상 선택하세요.'
      return
    }

    const factorVariants = factorLab.enabled
      ? await loadFactorLabCandidates(symbols, { silent: true })
      : []
    const researchPlans = buildBacktestResearchPlans({
      timingLab, universeComparison, universeBuilderSymbols, financialFactorSymbols
    }, symbols, factorVariants)

    if (timingLab.enabled && researchPlans.universeVariants.length === 0) {
      error = '현재 종목 입력에서 비교 가능한 유니버스가 없습니다. 필터 조건이나 종목 입력을 조정하세요.'
      return
    }

    running = true
    error = ''
    runStatus = ''
    comparisonResults = []
    activeScenarioKey = ''
    result = null
    try {
      if (timingLab.enabled) {
        const scenarios = researchPlans.scenarioPlans
        const nextResults = await runBacktestScenarios({
          startBacktest: (payload) => backtestApi.start(payload),
          form,
          scenarios,
          basePatterns: customPatterns,
          marketSymbol: timingLab.marketSymbol,
          onProgress: ({ current, total, scenario }) => {
            runStatus = `${current} / ${total} · ${scenario.label} 실행 중`
          }
        })

        comparisonResults = nextResults
        const defaultScenario = nextResults.find((item) => item.isBaseline) ?? nextResults[0]
        activeScenarioKey = defaultScenario?.key ?? ''
        result = defaultScenario?.data ?? null
        runStatus = `비교 시나리오 ${nextResults.length}개 실행 완료`
      } else {
        result = await runPlainBacktest({
          startBacktest: (payload) => backtestApi.start(payload),
          form,
          symbols,
          basePatterns: customPatterns
        })
      }
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '백테스트 실행에 실패했습니다.'
    } finally {
      running = false
    }
  }

  function resetForm() {
    result = null
    error = ''
    runStatus = ''
    comparisonResults = []
    activeScenarioKey = ''
    form = createBacktestForm(defaultSlippageModel)
    timingLab = createTimingLab()
    factorLab = createFactorLab()
    factorLabLoading = false
    factorLabError = ''
    factorLabSummaries = []
    factorLabVariants = []
    factorLabBaseSignature = ''
  }
</script>

<div class="flex-1 overflow-auto">
  <div class="space-y-8 p-8">
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-3xl font-bold">백테스트</h2>
        <p class="mt-2 text-sm text-gray-400">커스텀 패턴을 기준으로 과거 구간을 검증하고, 종목/레짐/워크포워드 결과까지 바로 확인합니다.</p>
      </div>
      <button on:click={resetForm} class="flex items-center gap-2 rounded bg-gray-800 px-4 py-2 text-sm text-white transition hover:bg-gray-700">
        <RotateCcw size={16} />
        초기화
      </button>
    </div>

    {#if error}
      <div class="rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
    {/if}

    <section class="grid grid-cols-1 gap-4 xl:grid-cols-2">
      <div class="rounded-2xl border border-blue-700/50 bg-blue-950/20 p-5">
        <div class="mb-2 text-sm font-semibold text-blue-200">지금 강한 연구 흐름</div>
        <div class="space-y-2 text-sm text-blue-50">
          <div>시장 타이밍 대칭/비대칭 비교</div>
          <div>시장 조건 + 개별 종목 조건 조합</div>
          <div>레짐 비중 전략과 패턴 조합 검증</div>
        </div>
      </div>
      <div class="rounded-2xl border border-amber-700 bg-amber-900/10 p-5">
        <div class="mb-2 text-sm font-semibold text-amber-200">아직 약한 연구 흐름</div>
        <div class="space-y-2 text-sm text-amber-100">
          <div>저PER / 턴어라운드 같은 재무 팩터</div>
          <div>분기 변화 기반 고급 팩터 랩</div>
          <div>외부 벤더 재무 데이터 동기화</div>
        </div>
      </div>
    </section>

    <FinancialFactorBuilder
      bind:symbolsText={form.symbolsText}
      bind:candidateSymbols={financialFactorSymbols}
      bind:selectionSummary={financialFactorSummary}
      bind:filterParams={financialFactorFilters}
      title="재무 팩터 빌더"
      description="저PER·흑자·턴어라운드·성장 조건으로 종목군을 다시 좁힌 뒤 타이밍 연구실에 연결합니다."
    />

    <UniverseBuilder
      bind:symbolsText={form.symbolsText}
      bind:candidateSymbols={universeBuilderSymbols}
      bind:selectionSummary={universeBuilderSummary}
      title="팩터·유니버스 빌더"
      description="시총 백분위, 섹터, 산업 기준으로 후보 종목군을 먼저 만든 뒤 타이밍 연구실에 바로 넘깁니다."
    />

    <section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
      <div class="flex items-center justify-between gap-4">
        <div>
          <h3 class="text-xl font-semibold">타이밍 연구실</h3>
          <p class="mt-2 text-sm text-gray-400">기본 패턴 위에 시장 타이밍, 시장+종목 타이밍 오버레이를 얹어 여러 시나리오를 한 번에 비교합니다.</p>
        </div>
        <label class="flex items-center gap-2 text-sm text-gray-300">
          <input type="checkbox" bind:checked={timingLab.enabled} />
          비교 모드 사용
        </label>
      </div>

        <div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-3">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">시장 참조 심볼</div>
          <input bind:value={timingLab.marketSymbol} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" placeholder="SPY" disabled={!timingLab.enabled} />
        </label>
        <label class="flex items-center gap-2 rounded-xl border border-gray-800 bg-gray-900 px-4 py-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={timingLab.includeBaseScenario} disabled={!timingLab.enabled} />
          기본 패턴 그대로도 같이 실행
        </label>
        <div class="rounded-xl border border-blue-700/40 bg-blue-950/20 px-4 py-3 text-sm text-blue-100">
          예상 시나리오 수: {viewModel.estimatedScenarioCount}개
        </div>
      </div>

      <BacktestUniverseControls
        timingEnabled={timingLab.enabled}
        {universeComparison}
        currentCount={viewModel.currentSymbolCount}
        universeCount={viewModel.universeSymbolCount}
        financialCount={viewModel.financialSymbolCount}
        combinedCount={viewModel.combinedSymbolCount}
        universeMatched={universeBuilderSummary?.matched ?? 0}
        financialMatched={financialFactorSummary?.matched ?? 0}
      />

      <BacktestFactorLabPanel
        {factorLab}
        loading={factorLabLoading}
        error={factorLabError}
        summaries={factorLabSummaries}
        presets={factorExperimentPresets}
        rankingOptions={factorRankingOptions}
        baseSymbolCount={viewModel.currentSymbolCount}
        selectionCount={viewModel.factorExperimentSelectionCount}
        onPreview={previewFactorLab}
        onTogglePreset={toggleFactorPreset}
        onAddCustom={addCustomFactorExperiment}
        onRemoveCustom={removeCustomFactorExperiment}
      />

      <BacktestTimingOptions
        {timingLab}
        structureOptions={timingStructureOptions}
        windowOptions={timingWindowOptions}
        onToggleStructure={toggleTimingStructure}
        onToggleWindow={toggleTimingWindow}
      />

      <div class="mt-4 rounded-xl border border-amber-700 bg-amber-900/10 p-4 text-sm text-amber-100">
        이 연구실은 선택한 패턴을 저장하지 않고 실행 시점에만 복제해서 타이밍 오버레이를 붙입니다. 빠른 청산 비교를 위해 타이밍 청산은 기존 청산 규칙과 <span class="font-semibold text-white">OR</span>로 합쳐집니다.
      </div>
    </section>

    <section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
      <h3 class="mb-5 text-xl font-semibold">실행 설정</h3>

      <BacktestExecutionInputs
        {form}
        {timeFrameOptions}
        {dataSourceOptions}
        {slippageOptions}
        warning={viewModel.timeframeWarning}
      />

      <BacktestRiskSettings {form} />

      <BacktestPatternSelection
        {patterns}
        {selectedPatternIds}
        {loading}
        {running}
        {runStatus}
        onToggle={togglePattern}
        onRun={runBacktest}
      />
    </section>

    {#if comparisonResults.length > 0}
      {#if viewModel.universeComparisonRows.length > 1}
        <BacktestUniverseComparison rows={viewModel.universeComparisonRows} />
      {/if}

      {#if viewModel.factorRankingRows.length > 0}
        <BacktestFactorRanking
          rows={viewModel.factorRankingRows}
          insightCards={viewModel.factorLabInsightCards}
          summary={viewModel.factorLabSummaryLine}
          rankingLabel={viewModel.factorRankingLabel}
        />
      {/if}

      <BacktestScenarioComparison
        rows={viewModel.scenarioComparisonRows}
        {activeScenarioKey}
        {runStatus}
        onSelect={selectScenarioResult}
      />
    {/if}

    {#if result}
      <section class="space-y-4">
        <BacktestResultSummary
          {result}
          timingReport={viewModel.timingReport}
        />

        <BacktestPerformanceBreakdown {result} />

        <BacktestValidationResults {result} />

        <BacktestTradeHistory trades={result.trades} />
      </section>
    {/if}
  </div>
</div>

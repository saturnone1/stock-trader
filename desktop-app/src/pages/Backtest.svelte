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
  import {
    buildFactorExperimentDefinitions as createFactorExperimentDefinitions,
    buildFactorSummaryTags,
    buildScenarioPatterns as createScenarioPatterns,
    buildTimingScenarios as createTimingScenarios,
    buildUniverseVariants as createUniverseVariants,
    combineScenarioPlans
  } from '../features/backtest/backtestScenarioPlanning'
  import {
    buildFactorLabInsightCards,
    buildFactorLabRankingRows,
    buildFactorLabSummaryLine,
    buildScenarioComparisonRows,
    buildTimingReport,
    buildUniverseComparisonRows
  } from '../features/backtest/backtestResultAnalysis'
  import {
    factorExperimentPresets,
    factorRankingOptions,
    intersectSymbols,
    timingStructureOptions,
    timingWindowOptions,
    uniqueSymbols
  } from '../features/backtest/backtestResearch'

  let timeFrameOptions = []
  let dataSourceOptions = [['', '기본 설정']]
  let dataProviders = []

  const slippageOptions = [
    ['Adaptive', '적응형'],
    ['Fixed', '고정 비율']
  ]

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

  let timingLab = {
    enabled: true,
    includeBaseScenario: true,
    marketSymbol: 'SPY',
    selectedStructures: ['market', 'market-stock'],
    selectedWindows: ['20-20', '20-10']
  }

  let universeComparison = {
    enabled: true,
    includeCurrentSymbols: true,
    includeUniverseBuilder: true,
    includeFinancialFactor: true,
    includeCombined: true
  }

  let factorLab = {
    enabled: false,
    selectedPresets: ['value-pe', 'quality-roe', 'turnaround-growth'],
    includeCurrentBuilder: true,
    minMatchedSymbols: 2,
    rankingMode: 'balanced',
    topRankedResults: 5,
    customExperiments: [
      {
        id: 'custom-1',
        label: '커스텀 조합 1',
        peRatioMax: '',
        pbRatioMax: '',
        roePercentMin: '',
        operatingMarginMin: '',
        revenueGrowthMin: '',
        netIncomeGrowthMin: '',
        positiveEarningsOnly: true,
        turnaroundOnly: false
      }
    ]
  }

  let form = {
    symbolsText: 'SPY, QQQ, TQQQ',
    from: '',
    to: '',
    initialCapital: 100000,
    timeFrame: 'Daily',
    dataSource: '',
    slippageModel: 'Adaptive',
    slippagePercent: 0.05,
    commissionPerTrade: 1,
    enableWalkForward: false,
    walkForwardInSampleMonths: 12,
    walkForwardOutOfSampleMonths: 3,
    enableMonteCarlo: false,
    monteCarloSimulations: 1000,
    riskPerTradePercent: 0.01,
    dailyLossLimitPercent: 0.03,
    maxTotalPositions: 7,
    maxPositionsPerSector: 2,
    useWeightStrategy: false,
    bullWeight: 1,
    bearWeight: 0.3,
    overheat1Weight: 0.7,
    overheat2Weight: 0.4,
    overheatStage1Pct: 1.15,
    overheatStage2Pct: 1.25,
    smaPeriod: 200
  }

  onMount(async () => {
    await Promise.all([loadMetadata(), loadPatterns()])
    loading = false
  })

  async function loadMetadata() {
    try {
      const metadata = await metadataApi.getStrategyBuilder()
      timeFrameOptions = (metadata?.timeFrames ?? []).map((item) => [item.value, item.displayName])
      dataProviders = metadata?.dataProviders ?? []
      dataSourceOptions = [['', '기본 설정'], ...dataProviders.map((item) => [item.value, item.displayName])]
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

  function parseSymbols() {
    return form.symbolsText.split(',').map((item) => item.trim().toUpperCase()).filter(Boolean)
  }

  function symbolSignature(symbols = parseSymbols()) {
    return uniqueSymbols(symbols).join('|')
  }

  function selectedPatterns() {
    return patterns.filter((pattern) => selectedPatternIds.includes(String(pattern.id)))
  }

  function togglePattern(id) {
    const value = String(id)
    selectedPatternIds = selectedPatternIds.includes(value)
      ? selectedPatternIds.filter((item) => item !== value)
      : [...selectedPatternIds, value]
  }

  function toggleTimingStructure(id) {
    timingLab.selectedStructures = timingLab.selectedStructures.includes(id)
      ? timingLab.selectedStructures.filter((item) => item !== id)
      : [...timingLab.selectedStructures, id]
  }

  function toggleTimingWindow(id) {
    timingLab.selectedWindows = timingLab.selectedWindows.includes(id)
      ? timingLab.selectedWindows.filter((item) => item !== id)
      : [...timingLab.selectedWindows, id]
  }

  function toggleFactorPreset(id) {
    factorLab.selectedPresets = factorLab.selectedPresets.includes(id)
      ? factorLab.selectedPresets.filter((item) => item !== id)
      : [...factorLab.selectedPresets, id]
  }

  function addCustomFactorExperiment() {
    factorLab.customExperiments = [
      ...factorLab.customExperiments,
      {
        id: `custom-${Date.now()}-${factorLab.customExperiments.length + 1}`,
        label: `커스텀 조합 ${factorLab.customExperiments.length + 1}`,
        peRatioMax: '',
        pbRatioMax: '',
        roePercentMin: '',
        operatingMarginMin: '',
        revenueGrowthMin: '',
        netIncomeGrowthMin: '',
        positiveEarningsOnly: true,
        turnaroundOnly: false
      }
    ]
  }

  function removeCustomFactorExperiment(id) {
    factorLab.customExperiments = factorLab.customExperiments.filter((item) => item.id !== id)
  }

  function timeframeWarning() {
    if (!form.from || !form.to) return ''
    const provider = dataProviders.find((item) => item.value === form.dataSource)
    const maxDays = provider?.maximumLookbackDays?.[form.timeFrame]
    const days = Math.max(0, Math.ceil((new Date(form.to).getTime() - new Date(form.from).getTime()) / 86400000))
    if (maxDays && days > maxDays) {
      const frameLabel = timeFrameOptions.find(([value]) => value === form.timeFrame)?.[1] ?? form.timeFrame
      return `${provider.displayName}의 ${frameLabel} 조회 한도는 최대 ${maxDays}일입니다.`
    }
    return ''
  }

  function buildScenarioPatterns(basePatterns, scenario) {
    return createScenarioPatterns(basePatterns, scenario, timingLab.marketSymbol)
  }

  function buildTimingScenarios() {
    return createTimingScenarios(timingLab, timingStructureOptions, timingWindowOptions)
  }

  function factorRankingLabel() {
    return factorRankingOptions.find((option) => option.id === factorLab.rankingMode)?.label ?? '균형 점수'
  }

  function buildFactorExperimentDefinitions() {
    return createFactorExperimentDefinitions(factorLab, factorExperimentPresets, financialFactorFilters)
  }

  function factorExperimentSelectionCount() {
    return buildFactorExperimentDefinitions().length
  }

  function factorLabVariantsFor(symbols) {
    return factorLab.enabled && factorLabBaseSignature === symbolSignature(symbols)
      ? factorLabVariants
      : []
  }

  async function loadFactorLabCandidates(baseSymbols, options = {}) {
    const { silent = false } = options
    const normalizedBaseSymbols = uniqueSymbols(baseSymbols)
    const baseSignature = symbolSignature(normalizedBaseSymbols)
    const definitions = buildFactorExperimentDefinitions()

    if (!factorLab.enabled || definitions.length === 0 || normalizedBaseSymbols.length === 0) {
      factorLabError = ''
      factorLabSummaries = []
      factorLabVariants = []
      factorLabBaseSignature = baseSignature
      return []
    }

    factorLabLoading = true
    try {
      const responses = await Promise.all(definitions.map(async (definition) => {
        const response = await financialFactorApi.query({
          ...definition.params,
          symbols: normalizedBaseSymbols.join(','),
          limit: Math.max(normalizedBaseSymbols.length, 20)
        })

        const matchedSymbols = uniqueSymbols((response.data?.items ?? []).map((item) => item.symbol))
        const filteredSummary = response.data?.comparison?.filtered ?? { count: 0, positiveEarningsCount: 0, turnaroundCount: 0 }

        return {
          definition,
          matchedSymbols,
          matched: response.data?.matched ?? matchedSymbols.length,
          filteredSummary,
          summaryTags: buildFactorSummaryTags(filteredSummary)
        }
      }))

      const validResponses = responses.filter(Boolean)
      factorLabSummaries = validResponses.map((item) => ({
        id: item.definition.id,
        label: item.definition.label,
        note: item.definition.note,
        source: item.definition.source,
        matched: item.matched,
        eligible: item.matched >= Number(factorLab.minMatchedSymbols),
        filteredSummary: item.filteredSummary,
        summaryTags: item.summaryTags
      }))
      factorLabVariants = validResponses
        .filter((item) => item.matchedSymbols.length >= Number(factorLab.minMatchedSymbols))
        .map((item) => ({
          key: `factorlab-${item.definition.id}`,
          kind: 'factor-lab',
          label: `팩터 실험 · ${item.definition.label}`,
          description: `${normalizedBaseSymbols.length}개 중 ${item.matchedSymbols.length}개가 ${item.definition.note} 조건을 만족합니다.`,
          symbols: item.matchedSymbols,
          symbolCount: item.matchedSymbols.length,
          factorPresetId: item.definition.id,
          factorPresetLabel: item.definition.label,
          factorPresetNote: item.definition.note
        }))
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
      await loadFactorLabCandidates(parseSymbols())
    } catch {
      // loadFactorLabCandidates already surfaces the error in factorLabError
    }
  }

  function buildUniverseVariants(baseSymbols, extraVariants = []) {
    return createUniverseVariants({ baseSymbols, extraVariants, universeComparison, universeBuilderSymbols, financialFactorSymbols })
  }

  function buildScenarioPlans(baseSymbols, extraVariants = []) {
    return combineScenarioPlans(buildUniverseVariants(baseSymbols, extraVariants), buildTimingScenarios())
  }

  function estimatedScenarioCount() {
    if (!timingLab.enabled) return 1
    return buildScenarioPlans(parseSymbols(), factorLabVariantsFor(parseSymbols())).length
  }

  function buildRequestPayload(symbols, customPatternRaws) {
    return {
      symbols,
      patterns: ['Custom'],
      from: form.from,
      to: form.to,
      initialCapital: Number(form.initialCapital),
      timeFrame: form.timeFrame,
      slippagePercent: Number(form.slippagePercent),
      commissionPerTrade: Number(form.commissionPerTrade),
      slippageModel: form.slippageModel,
      enableWalkForward: !!form.enableWalkForward,
      walkForwardInSampleMonths: Number(form.walkForwardInSampleMonths),
      walkForwardOutOfSampleMonths: Number(form.walkForwardOutOfSampleMonths),
      enableMonteCarlo: !!form.enableMonteCarlo,
      monteCarloSimulations: Number(form.monteCarloSimulations),
      riskPerTradePercent: Number(form.riskPerTradePercent),
      dailyLossLimitPercent: Number(form.dailyLossLimitPercent),
      maxTotalPositions: Number(form.maxTotalPositions),
      maxPositionsPerSector: Number(form.maxPositionsPerSector),
      dataSource: form.dataSource || null,
      weightStrategy: form.useWeightStrategy ? {
        bullWeight: Number(form.bullWeight),
        bearWeight: Number(form.bearWeight),
        overheat1Weight: Number(form.overheat1Weight),
        overheat2Weight: Number(form.overheat2Weight),
        overheatStage1Pct: Number(form.overheatStage1Pct),
        overheatStage2Pct: Number(form.overheatStage2Pct),
        smaPeriod: Number(form.smaPeriod)
      } : null,
      backtestMode: 'pattern',
      customPatterns: customPatternRaws
    }
  }

  async function runSingleBacktestRequest(symbols, basePatterns, scenario, customPatternRaws) {
    const response = await backtestApi.start(buildRequestPayload(symbols, customPatternRaws))
    return {
      key: scenario.key,
      label: scenario.label,
      description: scenario.description,
      structure: scenario.structure ?? 'base',
      windowId: scenario.windowId ?? 'base',
      comparisonGroupKey: scenario.comparisonGroupKey ?? 'current',
      comparisonGroupLabel: scenario.comparisonGroupLabel ?? '현재 입력',
      comparisonGroupKind: scenario.comparisonGroupKind ?? 'standard',
      symbolCount: scenario.symbolCount ?? symbols.length,
      factorPresetId: scenario.factorPresetId ?? null,
      factorPresetLabel: scenario.factorPresetLabel ?? null,
      factorPresetNote: scenario.factorPresetNote ?? null,
      isBaseline: scenario.type === 'base',
      data: {
        ...response.data,
        request: {
          symbols,
          patternNames: basePatterns.map((pattern) => pattern.name),
          universeVariant: {
            key: scenario.comparisonGroupKey ?? 'current',
            label: scenario.comparisonGroupLabel ?? '현재 입력',
            symbolCount: scenario.symbolCount ?? symbols.length,
            kind: scenario.comparisonGroupKind ?? 'standard',
            factorPresetLabel: scenario.factorPresetLabel ?? null
          }
        },
        timingScenario: {
          key: scenario.key,
          label: scenario.label,
          description: scenario.description
        }
      }
    }
  }

  function getTimingReport(entry) {
    return buildTimingReport(comparisonResults, entry)
  }

  function getActiveComparisonEntry() {
    return comparisonResults.find((item) => item.key === activeScenarioKey) ?? null
  }

  function getUniverseComparisonRows() {
    return buildUniverseComparisonRows(comparisonResults)
  }

  function getFactorLabRankingRows() {
    return buildFactorLabRankingRows(comparisonResults, factorLabSummaries, factorLab.rankingMode, factorLab.topRankedResults)
  }

  function getFactorLabInsightCards() {
    return buildFactorLabInsightCards(getFactorLabRankingRows())
  }

  function getFactorLabSummaryLine() {
    return buildFactorLabSummaryLine(getFactorLabRankingRows())
  }

  function selectScenarioResult(key) {
    const next = comparisonResults.find((item) => item.key === key)
    if (!next) return
    activeScenarioKey = key
    result = next.data
  }

  function getScenarioComparisonRows() {
    return buildScenarioComparisonRows(comparisonResults)
  }

  async function runBacktest() {
    const symbols = parseSymbols()
    const customPatterns = selectedPatterns()
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

    if (timingLab.enabled && buildUniverseVariants(symbols, factorVariants).length === 0) {
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
        const scenarios = buildScenarioPlans(symbols, factorVariants)
        const nextResults = []

        for (let index = 0; index < scenarios.length; index += 1) {
          const scenario = scenarios[index]
          runStatus = `${index + 1} / ${scenarios.length} · ${scenario.label} 실행 중`
          const scenarioPatterns = buildScenarioPatterns(customPatterns, scenario)
          const scenarioResult = await runSingleBacktestRequest(scenario.symbols, customPatterns, scenario, scenarioPatterns)
          nextResults.push(scenarioResult)
        }

        comparisonResults = nextResults
        const defaultScenario = nextResults.find((item) => item.isBaseline) ?? nextResults[0]
        activeScenarioKey = defaultScenario?.key ?? ''
        result = defaultScenario?.data ?? null
        runStatus = `비교 시나리오 ${nextResults.length}개 실행 완료`
      } else {
        const response = await backtestApi.start(buildRequestPayload(symbols, customPatterns.map((pattern) => pattern.raw)))
        result = {
          ...response.data,
          request: {
            symbols,
            patternNames: customPatterns.map((pattern) => pattern.name)
          }
        }
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
    form = {
      ...form,
      symbolsText: 'SPY, QQQ, TQQQ',
      from: '',
      to: '',
      initialCapital: 100000,
      timeFrame: 'Daily',
      dataSource: '',
      slippageModel: 'Adaptive',
      slippagePercent: 0.05,
      commissionPerTrade: 1,
      enableWalkForward: false,
      walkForwardInSampleMonths: 12,
      walkForwardOutOfSampleMonths: 3,
      enableMonteCarlo: false,
      monteCarloSimulations: 1000,
      riskPerTradePercent: 0.01,
      dailyLossLimitPercent: 0.03,
      maxTotalPositions: 7,
      maxPositionsPerSector: 2,
      useWeightStrategy: false,
      bullWeight: 1,
      bearWeight: 0.3,
      overheat1Weight: 0.7,
      overheat2Weight: 0.4,
      overheatStage1Pct: 1.15,
      overheatStage2Pct: 1.25,
      smaPeriod: 200
    }
    timingLab = {
      enabled: true,
      includeBaseScenario: true,
      marketSymbol: 'SPY',
      selectedStructures: ['market', 'market-stock'],
      selectedWindows: ['20-20', '20-10']
    }
    factorLab = {
      enabled: false,
      selectedPresets: ['value-pe', 'quality-roe', 'turnaround-growth'],
      includeCurrentBuilder: true,
      minMatchedSymbols: 2,
      rankingMode: 'balanced',
      topRankedResults: 5,
      customExperiments: [
        {
          id: 'custom-1',
          label: '커스텀 조합 1',
          peRatioMax: '',
          pbRatioMax: '',
          roePercentMin: '',
          operatingMarginMin: '',
          revenueGrowthMin: '',
          netIncomeGrowthMin: '',
          positiveEarningsOnly: true,
          turnaroundOnly: false
        }
      ]
    }
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
          예상 시나리오 수: {estimatedScenarioCount()}개
        </div>
      </div>

      <BacktestUniverseControls
        timingEnabled={timingLab.enabled}
        {universeComparison}
        currentCount={uniqueSymbols(parseSymbols()).length}
        universeCount={intersectSymbols(parseSymbols(), universeBuilderSymbols).length}
        financialCount={intersectSymbols(parseSymbols(), financialFactorSymbols).length}
        combinedCount={intersectSymbols(intersectSymbols(parseSymbols(), universeBuilderSymbols), financialFactorSymbols).length}
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
        baseSymbolCount={uniqueSymbols(parseSymbols()).length}
        selectionCount={factorExperimentSelectionCount()}
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
        warning={timeframeWarning()}
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
      {@const universeComparisonRows = getUniverseComparisonRows()}
      {#if universeComparisonRows.length > 1}
        <BacktestUniverseComparison rows={universeComparisonRows} />
      {/if}

      {@const factorRankingRows = getFactorLabRankingRows()}
      {#if factorRankingRows.length > 0}
        <BacktestFactorRanking
          rows={factorRankingRows}
          insightCards={getFactorLabInsightCards()}
          summary={getFactorLabSummaryLine()}
          rankingLabel={factorRankingLabel()}
        />
      {/if}

      <BacktestScenarioComparison
        rows={getScenarioComparisonRows()}
        {activeScenarioKey}
        {runStatus}
        onSelect={selectScenarioResult}
      />
    {/if}

    {#if result}
      <section class="space-y-4">
        <BacktestResultSummary
          {result}
          timingReport={getTimingReport(getActiveComparisonEntry())}
        />

        <BacktestPerformanceBreakdown {result} />

        <BacktestValidationResults {result} />

        <BacktestTradeHistory trades={result.trades} />
      </section>
    {/if}
  </div>
</div>

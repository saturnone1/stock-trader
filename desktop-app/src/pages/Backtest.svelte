<script>
  import { onMount } from 'svelte'
  import { Play, RotateCcw, TriangleAlert } from 'lucide-svelte'
  import { backtestApi, financialFactorApi, metadataApi, patternApi } from '../api/endpoints'
  import FinancialFactorBuilder from '../lib/FinancialFactorBuilder.svelte'
  import UniverseBuilder from '../lib/UniverseBuilder.svelte'
  import BacktestFactorRanking from '../features/backtest/BacktestFactorRanking.svelte'
  import BacktestPerformanceBreakdown from '../features/backtest/BacktestPerformanceBreakdown.svelte'
  import BacktestResultSummary from '../features/backtest/BacktestResultSummary.svelte'
  import BacktestTradeHistory from '../features/backtest/BacktestTradeHistory.svelte'
  import BacktestValidationResults from '../features/backtest/BacktestValidationResults.svelte'
  import {
    factorExperimentPresets,
    factorDrawdownImprovement,
    factorRankingOptions,
    factorReturnLift,
    factorSourceLabel,
    formatDecimal,
    formatPercent,
    formatSignedPercent,
    getEquityCurveVolatility,
    getWhipsawStats,
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

  function safeParseJson(value, fallback) {
    try {
      return value ? JSON.parse(value) : fallback
    } catch {
      return fallback
    }
  }

  function cloneDeep(value) {
    return JSON.parse(JSON.stringify(value))
  }

  function buildTimingRule({ period, operator, refSymbol = '' }) {
    return {
      indicator: 'PRICE_VS_SMA',
      params: { period },
      operator,
      value: 0,
      withinBars: 0,
      refSymbol,
      compareIndicator: '',
      compareParams: {},
      weight: 1,
      consecutiveBars: 0
    }
  }

  function normalizeEntryGroups(rawPattern) {
    const entryGroups = safeParseJson(rawPattern.entryGroupsJson, [])
    if (entryGroups.length > 0) return cloneDeep(entryGroups)

    const flatRules = safeParseJson(rawPattern.entryRulesJson, [])
    if (flatRules.length > 0) {
      return [{
        label: '기존 진입 규칙',
        logic: rawPattern.entryLogic ?? 'AND',
        rules: cloneDeep(flatRules)
      }]
    }

    return [{
      label: '기본 진입 그룹',
      logic: 'AND',
      rules: []
    }]
  }

  function normalizeExitRules(rawPattern) {
    return cloneDeep(safeParseJson(rawPattern.exitRulesJson, []))
  }

  function buildScenarioPatterns(basePatterns, scenario) {
    if (scenario.type === 'base') {
      return basePatterns.map((pattern) => cloneDeep(pattern.raw))
    }

    const marketSymbol = (timingLab.marketSymbol || 'SPY').trim().toUpperCase() || 'SPY'

    return basePatterns.map((pattern) => {
      const raw = cloneDeep(pattern.raw)
      const entryGroups = normalizeEntryGroups(raw)
      const exitRules = normalizeExitRules(raw)

      for (const group of entryGroups) {
        group.rules = group.rules ?? []
        group.rules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '>', refSymbol: marketSymbol }))
        if (scenario.structure === 'market-stock') {
          group.rules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '>' }))
        }
      }

      exitRules.push(buildTimingRule({ period: scenario.exitPeriod, operator: '<', refSymbol: marketSymbol }))
      if (scenario.structure === 'market-stock') {
        exitRules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '<' }))
      }

      raw.name = `${pattern.name} · ${scenario.label}`
      raw.entryGroupsJson = JSON.stringify(entryGroups)
      raw.entryRulesJson = JSON.stringify([])
      raw.entryGroupsLogic = raw.entryGroupsLogic ?? raw.entryLogic ?? 'AND'
      raw.exitRulesJson = JSON.stringify(exitRules)
      raw.exitRulesLogic = 'OR'
      raw.requireBullRegime = false
      return raw
    })
  }

  function buildTimingScenarios() {
    const scenarios = []

    if (timingLab.includeBaseScenario) {
      scenarios.push({
        key: 'base',
        type: 'base',
        label: '기본 패턴 그대로',
        description: '현재 선택한 패턴을 수정 없이 그대로 실행합니다.'
      })
    }

    for (const structure of timingLab.selectedStructures) {
      const structureLabel = timingStructureOptions.find((item) => item.id === structure)?.label ?? structure
      for (const windowId of timingLab.selectedWindows) {
        const windowConfig = timingWindowOptions.find((item) => item.id === windowId)
        if (!windowConfig) continue
        scenarios.push({
          key: `${structure}-${windowConfig.id}`,
          type: 'overlay',
          structure,
          windowId: windowConfig.id,
          entryPeriod: windowConfig.entryPeriod,
          exitPeriod: windowConfig.exitPeriod,
          label: `${structureLabel} ${windowConfig.label}`,
          description: `${structureLabel}에 ${windowConfig.label} 기간을 적용합니다.`
        })
      }
    }

    return scenarios
  }

  function buildFactorSummaryTags(filteredSummary) {
    const tags = []
    if (filteredSummary?.averagePe != null) tags.push(`평균 PER ${formatDecimal(filteredSummary.averagePe)}`)
    if (filteredSummary?.averagePb != null) tags.push(`평균 PBR ${formatDecimal(filteredSummary.averagePb)}`)
    if (filteredSummary?.averageRoe != null) tags.push(`평균 ROE ${formatDecimal(filteredSummary.averageRoe)}%`)
    if (filteredSummary?.turnaroundCount) tags.push(`턴어라운드 ${filteredSummary.turnaroundCount}개`)
    return tags.slice(0, 3)
  }

  function factorRankingLabel() {
    return factorRankingOptions.find((option) => option.id === factorLab.rankingMode)?.label ?? '균형 점수'
  }

  function normalizeFactorExperimentParams(params = {}) {
    const normalized = {}
    for (const [key, value] of Object.entries(params)) {
      if (key === 'limit' || key === 'sortBy' || value == null) continue
      if (typeof value === 'string' && value.trim() === '') continue
      if (typeof value === 'boolean' && !value) continue
      normalized[key] = typeof value === 'string' ? value.trim() : value
    }
    return normalized
  }

  function buildFactorExperimentDefinitions() {
    const definitions = []
    const seen = new Set()

    function addDefinition(definition) {
      const params = normalizeFactorExperimentParams(definition?.params ?? {})
      if (Object.keys(params).length === 0) return
      const signature = JSON.stringify(params)
      if (seen.has(signature)) return
      seen.add(signature)
      definitions.push({ ...definition, params })
    }

    for (const presetId of factorLab.selectedPresets) {
      const preset = factorExperimentPresets.find((item) => item.id === presetId)
      if (!preset) continue
      addDefinition({
        id: preset.id,
        label: preset.label,
        note: preset.note,
        source: 'preset',
        params: preset.params
      })
    }

    if (factorLab.includeCurrentBuilder) {
      addDefinition({
        id: 'current-builder',
        label: '현재 재무 팩터 빌더',
        note: '재무 팩터 빌더에서 지금 잡아둔 조건 그대로',
        source: 'current-builder',
        params: financialFactorFilters ?? {}
      })
    }

    for (const experiment of factorLab.customExperiments) {
      addDefinition({
        id: experiment.id,
        label: experiment.label?.trim() || '커스텀 조합',
        note: '사용자 정의 재무 팩터 조합',
        source: 'custom',
        params: {
          peRatioMax: experiment.peRatioMax,
          pbRatioMax: experiment.pbRatioMax,
          roePercentMin: experiment.roePercentMin,
          operatingMarginMin: experiment.operatingMarginMin,
          revenueGrowthMin: experiment.revenueGrowthMin,
          netIncomeGrowthMin: experiment.netIncomeGrowthMin,
          positiveEarningsOnly: experiment.positiveEarningsOnly,
          turnaroundOnly: experiment.turnaroundOnly
        }
      })
    }

    return definitions
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

  function factorScenarioScore(entry) {
    const totalReturn = Number(entry?.data?.totalReturn ?? 0)
    const sharpeRatio = Number(entry?.data?.sharpeRatio ?? 0)
    const maxDrawdown = Number(entry?.data?.maxDrawdown ?? 0)
    const totalTrades = Number(entry?.data?.totalTrades ?? 0)

    switch (factorLab.rankingMode) {
      case 'best-return':
        return (totalReturn * 140) + (sharpeRatio * 12) - (maxDrawdown * 35) + Math.min(totalTrades, 80) * 0.04
      case 'best-sharpe':
        return (sharpeRatio * 120) + (totalReturn * 55) - (maxDrawdown * 45) + Math.min(totalTrades, 80) * 0.05
      case 'defensive':
        return (totalReturn * 60) + (sharpeRatio * 40) - (maxDrawdown * 95) + Math.min(totalTrades, 80) * 0.03
      case 'balanced':
      default:
        return (totalReturn * 95) + (sharpeRatio * 32) - (maxDrawdown * 60) + Math.min(totalTrades, 80) * 0.04
    }
  }

  function buildUniverseVariants(baseSymbols, extraVariants = []) {
    const normalizedBaseSymbols = uniqueSymbols(baseSymbols)
    const variants = []
    const seen = new Set()

    function addVariant(variant) {
      if (!variant?.symbols?.length) return
      const signature = uniqueSymbols(variant.symbols).join('|')
      if (!signature || seen.has(signature)) return
      seen.add(signature)
      variants.push({
        ...variant,
        symbols: uniqueSymbols(variant.symbols),
        symbolCount: uniqueSymbols(variant.symbols).length
      })
    }

    if (universeComparison.includeCurrentSymbols) {
      addVariant({
        key: 'current',
        label: '현재 입력',
        description: '현재 종목 입력을 그대로 실행합니다.',
        symbols: normalizedBaseSymbols
      })
    }

    if (universeComparison.enabled && universeComparison.includeUniverseBuilder) {
      const filtered = intersectSymbols(normalizedBaseSymbols, universeBuilderSymbols)
      addVariant({
        key: 'universe',
        label: '시총·섹터 필터',
        description: `${normalizedBaseSymbols.length}개 중 ${filtered.length}개가 유니버스 빌더 조건에 남았습니다.`,
        symbols: filtered
      })
    }

    if (universeComparison.enabled && universeComparison.includeFinancialFactor) {
      const filtered = intersectSymbols(normalizedBaseSymbols, financialFactorSymbols)
      addVariant({
        key: 'financial',
        label: '재무 팩터 필터',
        description: `${normalizedBaseSymbols.length}개 중 ${filtered.length}개가 재무 팩터 조건에 남았습니다.`,
        symbols: filtered
      })
    }

    if (universeComparison.enabled && universeComparison.includeCombined) {
      const combined = intersectSymbols(intersectSymbols(normalizedBaseSymbols, universeBuilderSymbols), financialFactorSymbols)
      addVariant({
        key: 'combined',
        label: '교집합 필터',
        description: '시총·섹터·재무 팩터 조건을 모두 만족한 종목만 실행합니다.',
        symbols: combined
      })
    }

    for (const variant of extraVariants) {
      addVariant(variant)
    }

    return variants
  }

  function buildScenarioPlans(baseSymbols, extraVariants = []) {
    const universeVariants = buildUniverseVariants(baseSymbols, extraVariants)
    const timingScenarios = buildTimingScenarios()
    const plans = []

    for (const variant of universeVariants) {
      for (const scenario of timingScenarios) {
        plans.push({
          ...scenario,
          key: `${variant.key}::${scenario.key}`,
          label: `${variant.label} · ${scenario.label}`,
          description: `${variant.description} ${scenario.description}`,
          comparisonGroupKey: variant.key,
          comparisonGroupLabel: variant.label,
          comparisonGroupKind: variant.kind ?? 'standard',
          symbols: variant.symbols,
          symbolCount: variant.symbolCount,
          factorPresetId: variant.factorPresetId ?? null,
          factorPresetLabel: variant.factorPresetLabel ?? null,
          factorPresetNote: variant.factorPresetNote ?? null
        })
      }
    }

    return plans
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

  function getBaselineEntry(groupKey = 'current') {
    return comparisonResults.find((item) => item.comparisonGroupKey === groupKey && item.isBaseline)
      ?? comparisonResults.find((item) => item.isBaseline)
      ?? null
  }

  function getBaselineResult(entry = null) {
    const groupKey = entry?.comparisonGroupKey ?? 'current'
    return getBaselineEntry(groupKey)?.data ?? null
  }

  function getComparisonDelta(entry) {
    const baseline = getBaselineResult(entry)
    if (!baseline || entry.isBaseline) return null

    const baselineWhipsaw = getWhipsawStats(baseline)
    const currentWhipsaw = getWhipsawStats(entry.data)
    const baselineVolatility = getEquityCurveVolatility(baseline)
    const currentVolatility = getEquityCurveVolatility(entry.data)

    return {
      returnDelta: Number(entry.data.totalReturn ?? 0) - Number(baseline.totalReturn ?? 0),
      drawdownImprovement: Number(baseline.maxDrawdown ?? 0) - Number(entry.data.maxDrawdown ?? 0),
      tradeReduction: Number(baseline.totalTrades ?? 0) - Number(entry.data.totalTrades ?? 0),
      whipsawReduction: baselineWhipsaw.count - currentWhipsaw.count,
      whipsawRateImprovement: baselineWhipsaw.rate - currentWhipsaw.rate,
      stabilityImprovement: baselineVolatility != null && currentVolatility != null
        ? baselineVolatility - currentVolatility
        : null
    }
  }

  function getTimingReport(entry) {
    const baseline = getBaselineResult(entry)
    if (!baseline || !entry || entry.isBaseline) return null

    const delta = getComparisonDelta(entry)
    const currentWhipsaw = getWhipsawStats(entry.data)
    const currentVolatility = getEquityCurveVolatility(entry.data)

    return {
      drawdownImprovement: delta?.drawdownImprovement ?? 0,
      tradeReduction: delta?.tradeReduction ?? 0,
      whipsawReduction: delta?.whipsawReduction ?? 0,
      whipsawRateImprovement: delta?.whipsawRateImprovement ?? 0,
      stabilityImprovement: delta?.stabilityImprovement,
      currentWhipsawRate: currentWhipsaw.rate,
      currentWhipsawCount: currentWhipsaw.count,
      currentVolatility
    }
  }

  function getActiveComparisonEntry() {
    return comparisonResults.find((item) => item.key === activeScenarioKey) ?? null
  }

  function getUniverseComparisonRows() {
    const baseEntry = getBaselineEntry('current')
    const baseSymbols = baseEntry?.data?.request?.symbols?.length ?? 0
    const rows = []
    const seen = new Set()

    for (const entry of comparisonResults) {
      if (seen.has(entry.comparisonGroupKey)) continue
      const baselineEntry = getBaselineEntry(entry.comparisonGroupKey)
      if (!baselineEntry) continue

      seen.add(entry.comparisonGroupKey)
      rows.push({
        key: entry.comparisonGroupKey,
        label: entry.comparisonGroupLabel,
        symbolCount: baselineEntry.data.request.symbols.length,
        symbolReduction: baseSymbols ? baseSymbols - baselineEntry.data.request.symbols.length : 0,
        totalReturn: baselineEntry.data.totalReturn,
        maxDrawdown: baselineEntry.data.maxDrawdown,
        sharpeRatio: baselineEntry.data.sharpeRatio,
        totalTrades: baselineEntry.data.totalTrades
      })
    }

    return rows
  }

  function compareScenarioPerformance(left, right) {
    const scoreDiff = factorScenarioScore(right) - factorScenarioScore(left)
    if (Math.abs(scoreDiff) > 0.000001) return scoreDiff

    const returnDiff = Number(right?.data?.totalReturn ?? 0) - Number(left?.data?.totalReturn ?? 0)
    if (Math.abs(returnDiff) > 0.000001) return returnDiff

    return Number(right?.data?.sharpeRatio ?? 0) - Number(left?.data?.sharpeRatio ?? 0)
  }

  function getFactorLabRankingRows() {
    const grouped = new Map()

    for (const entry of comparisonResults.filter((item) => item.comparisonGroupKind === 'factor-lab')) {
      const current = grouped.get(entry.comparisonGroupKey) ?? []
      current.push(entry)
      grouped.set(entry.comparisonGroupKey, current)
    }

    const rows = [...grouped.entries()].map(([groupKey, entries]) => {
      const baselineEntry = entries.find((item) => item.isBaseline) ?? entries[0]
      const bestEntry = [...entries].sort(compareScenarioPerformance)[0] ?? baselineEntry
      const summary = factorLabSummaries.find((item) => item.id === baselineEntry.factorPresetId)

      return {
        key: groupKey,
        label: baselineEntry.factorPresetLabel ?? baselineEntry.comparisonGroupLabel,
        note: summary?.note ?? baselineEntry.factorPresetNote ?? '',
        summaryTags: summary?.summaryTags ?? [],
        source: summary?.source ?? 'preset',
        symbolCount: baselineEntry.symbolCount,
        baselineReturn: baselineEntry.data.totalReturn,
        baselineDrawdown: baselineEntry.data.maxDrawdown,
        bestScenarioLabel: bestEntry.data?.timingScenario?.label ?? bestEntry.label,
        bestReturn: bestEntry.data.totalReturn,
        bestDrawdown: bestEntry.data.maxDrawdown,
        bestSharpe: bestEntry.data.sharpeRatio,
        bestTrades: bestEntry.data.totalTrades,
        bestScore: factorScenarioScore(bestEntry)
      }
    }).sort((left, right) => {
      const scoreDiff = Number(right.bestScore ?? 0) - Number(left.bestScore ?? 0)
      if (Math.abs(scoreDiff) > 0.000001) return scoreDiff

      return Number(right.bestReturn ?? 0) - Number(left.bestReturn ?? 0)
    })

    return rows
      .slice(0, Math.max(1, Number(factorLab.topRankedResults ?? 5)))
      .map((row, index) => ({ ...row, rank: index + 1 }))
  }

  function getFactorLabInsightCards() {
    const rows = getFactorLabRankingRows()
    if (!rows.length) return []

    const winner = rows[0]
    const biggestLift = [...rows].sort((left, right) => factorReturnLift(right) - factorReturnLift(left))[0] ?? winner
    const strongestDefense = [...rows].sort((left, right) => factorDrawdownImprovement(right) - factorDrawdownImprovement(left))[0] ?? winner
    const highestSharpe = [...rows].sort((left, right) => Number(right.bestSharpe ?? 0) - Number(left.bestSharpe ?? 0))[0] ?? winner

    return [
      {
        key: 'winner',
        label: '현재 우승 조합',
        headline: winner.label,
        detail: `${winner.bestScenarioLabel} · 점수 ${formatDecimal(winner.bestScore)}`,
        accent: 'text-fuchsia-200'
      },
      {
        key: 'lift',
        label: '기준선 대비 최대 수익 개선',
        headline: formatSignedPercent(factorReturnLift(biggestLift)),
        detail: `${biggestLift.label} · ${biggestLift.bestScenarioLabel}`,
        accent: factorReturnLift(biggestLift) >= 0 ? 'text-emerald-300' : 'text-red-300'
      },
      {
        key: 'defense',
        label: '가장 방어적인 조합',
        headline: formatSignedPercent(factorDrawdownImprovement(strongestDefense)),
        detail: `${strongestDefense.label} · 낙폭 ${formatPercent(strongestDefense.bestDrawdown)}`,
        accent: factorDrawdownImprovement(strongestDefense) >= 0 ? 'text-cyan-300' : 'text-red-300'
      },
      {
        key: 'sharpe',
        label: '최고 샤프 조합',
        headline: formatDecimal(highestSharpe.bestSharpe),
        detail: `${highestSharpe.label} · ${highestSharpe.bestScenarioLabel}`,
        accent: 'text-blue-300'
      }
    ]
  }

  function getFactorLabSummaryLine() {
    const rows = getFactorLabRankingRows()
    if (!rows.length) return ''

    const averageSymbols = rows.reduce((sum, row) => sum + Number(row.symbolCount ?? 0), 0) / rows.length
    const positiveLiftCount = rows.filter((row) => factorReturnLift(row) > 0).length
    return `상위 ${rows.length}개 조합 중 ${positiveLiftCount}개가 기준선 대비 수익률을 개선했고, 평균 종목 수는 ${averageSymbols.toFixed(1)}개입니다.`
  }

  function selectScenarioResult(key) {
    const next = comparisonResults.find((item) => item.key === key)
    if (!next) return
    activeScenarioKey = key
    result = next.data
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

      <div class="mt-5 rounded-xl border border-emerald-800/40 bg-emerald-950/20 p-5">
        <div class="flex items-center justify-between gap-4">
          <div>
            <div class="text-sm font-semibold text-emerald-100">유니버스·팩터 비교</div>
            <div class="mt-1 text-sm text-emerald-50">현재 입력 종목군에서 시총/섹터 필터, 재무 팩터 필터, 교집합 필터를 직접 잘라 같은 타이밍 시나리오로 비교합니다.</div>
          </div>
          <label class="flex items-center gap-2 text-sm text-emerald-100">
            <input type="checkbox" bind:checked={universeComparison.enabled} disabled={!timingLab.enabled} />
            필터 비교 포함
          </label>
        </div>

      <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
          <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300">
            <input type="checkbox" bind:checked={universeComparison.includeCurrentSymbols} disabled={!timingLab.enabled} />
            현재 입력 유지
          </label>
          <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300">
            <input type="checkbox" bind:checked={universeComparison.includeUniverseBuilder} disabled={!timingLab.enabled || !universeComparison.enabled} />
            시총·섹터 필터
          </label>
          <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300">
            <input type="checkbox" bind:checked={universeComparison.includeFinancialFactor} disabled={!timingLab.enabled || !universeComparison.enabled} />
            재무 팩터 필터
          </label>
          <label class="flex items-center gap-2 rounded border border-emerald-800/40 bg-gray-950 px-4 py-3 text-sm text-gray-300">
            <input type="checkbox" bind:checked={universeComparison.includeCombined} disabled={!timingLab.enabled || !universeComparison.enabled} />
            교집합 필터
          </label>
        </div>

        <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">현재 입력</div>
            <div class="mt-2 text-xl font-semibold text-white">{uniqueSymbols(parseSymbols()).length}</div>
          </div>
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">시총·섹터 필터 후</div>
            <div class="mt-2 text-xl font-semibold text-white">{intersectSymbols(parseSymbols(), universeBuilderSymbols).length}</div>
            <div class="mt-1 text-xs text-gray-500">후보 {universeBuilderSummary?.matched ?? 0}개</div>
          </div>
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">재무 팩터 후</div>
            <div class="mt-2 text-xl font-semibold text-white">{intersectSymbols(parseSymbols(), financialFactorSymbols).length}</div>
            <div class="mt-1 text-xs text-gray-500">후보 {financialFactorSummary?.matched ?? 0}개</div>
          </div>
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">교집합 필터 후</div>
            <div class="mt-2 text-xl font-semibold text-white">{intersectSymbols(intersectSymbols(parseSymbols(), universeBuilderSymbols), financialFactorSymbols).length}</div>
            <div class="mt-1 text-xs text-gray-500">타이밍 비교에 바로 투입</div>
          </div>
        </div>
      </div>

      <div class="mt-5 rounded-xl border border-fuchsia-800/40 bg-fuchsia-950/20 p-5">
        <div class="flex items-start justify-between gap-4">
          <div>
            <div class="text-sm font-semibold text-fuchsia-100">팩터 실험실</div>
            <div class="mt-1 text-sm text-fuchsia-50">현재 종목 입력을 기준으로 여러 재무 팩터 조합을 자동 생성하고, 같은 타이밍 시나리오로 돌린 뒤 어떤 조합이 더 나았는지 랭킹합니다.</div>
          </div>
          <div class="flex items-center gap-3">
            <label class="flex items-center gap-2 text-sm text-fuchsia-100">
              <input type="checkbox" bind:checked={factorLab.enabled} />
              실험실 사용
            </label>
            <button on:click={previewFactorLab} disabled={!factorLab.enabled || factorLabLoading || parseSymbols().length === 0 || factorExperimentSelectionCount() === 0} class="rounded bg-fuchsia-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-fuchsia-700 disabled:opacity-40">
              {factorLabLoading ? '불러오는 중...' : '후보 미리보기'}
            </button>
          </div>
        </div>

        {#if factorLabError}
          <div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{factorLabError}</div>
        {/if}

        <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-3">
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">기준 종목군</div>
            <div class="mt-2 text-xl font-semibold text-white">{uniqueSymbols(parseSymbols()).length}</div>
          </div>
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">선택한 실험 조합</div>
            <div class="mt-2 text-xl font-semibold text-white">{factorExperimentSelectionCount()}</div>
          </div>
          <div class="rounded border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="text-xs text-gray-500">실행 가능한 프리셋</div>
            <div class="mt-2 text-xl font-semibold text-white">{factorLabSummaries.filter((item) => item.eligible).length}</div>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-4">
          <label class="flex items-center gap-2 rounded border border-fuchsia-800/30 bg-gray-950 px-4 py-3 text-sm text-gray-300">
            <input type="checkbox" bind:checked={factorLab.includeCurrentBuilder} disabled={!factorLab.enabled} />
            현재 재무 팩터 빌더 조건 포함
          </label>
          <label class="text-sm text-gray-300">
            <div class="mb-2 text-gray-500">최소 종목 수</div>
            <input type="number" min="1" max="1000" bind:value={factorLab.minMatchedSymbols} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled} />
          </label>
          <label class="text-sm text-gray-300">
            <div class="mb-2 text-gray-500">랭킹 기준</div>
            <select bind:value={factorLab.rankingMode} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled}>
              {#each factorRankingOptions as option}
                <option value={option.id}>{option.label}</option>
              {/each}
            </select>
          </label>
          <label class="text-sm text-gray-300">
            <div class="mb-2 text-gray-500">랭킹 표시 개수</div>
            <input type="number" min="1" max="20" bind:value={factorLab.topRankedResults} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" disabled={!factorLab.enabled} />
          </label>
        </div>

        <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-3">
          {#each factorExperimentPresets as preset}
            <label class={`rounded-xl border px-4 py-3 text-sm transition ${factorLab.selectedPresets.includes(preset.id) ? 'border-fuchsia-500 bg-fuchsia-950/30 text-fuchsia-50' : 'border-gray-800 bg-gray-950 text-gray-300'}`}>
              <div class="flex items-start gap-3">
                <input type="checkbox" checked={factorLab.selectedPresets.includes(preset.id)} on:change={() => toggleFactorPreset(preset.id)} disabled={!factorLab.enabled} class="mt-1" />
                <div>
                  <div class="font-medium text-white">{preset.label}</div>
                  <div class="mt-1 text-xs text-gray-400">{preset.note}</div>
                </div>
              </div>
            </label>
          {/each}
        </div>

        <div class="mt-4 rounded-xl border border-fuchsia-800/30 bg-gray-950 p-4">
          <div class="mb-3 flex items-center justify-between gap-4">
            <div>
              <div class="text-sm font-semibold text-white">커스텀 팩터 조합</div>
              <div class="mt-1 text-xs text-gray-400">PER/PBR/ROE/마진/성장/턴어라운드 조건을 직접 섞어서 배치 실험용 조합을 여러 개 만듭니다.</div>
            </div>
            <button on:click={addCustomFactorExperiment} disabled={!factorLab.enabled} class="rounded bg-fuchsia-700 px-3 py-2 text-sm font-medium text-white transition hover:bg-fuchsia-600 disabled:opacity-40">
              커스텀 조합 추가
            </button>
          </div>

          <div class="space-y-4">
            {#each factorLab.customExperiments as experiment, index (experiment.id)}
              <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                <div class="mb-3 flex items-center justify-between gap-4">
                  <label class="flex-1 text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">조합 이름</div>
                    <input bind:value={experiment.label} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder={`커스텀 조합 ${index + 1}`} />
                  </label>
                  <button on:click={() => removeCustomFactorExperiment(experiment.id)} disabled={!factorLab.enabled || factorLab.customExperiments.length === 1} class="mt-6 rounded border border-red-700 px-3 py-2 text-sm text-red-300 transition hover:bg-red-950/30 disabled:opacity-40">
                    제거
                  </button>
                </div>

                <div class="grid grid-cols-1 gap-3 xl:grid-cols-3">
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">PER 최대</div>
                    <input bind:value={experiment.peRatioMax} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 15" />
                  </label>
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">PBR 최대</div>
                    <input bind:value={experiment.pbRatioMax} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 1.5" />
                  </label>
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">ROE 최소 (%)</div>
                    <input bind:value={experiment.roePercentMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 12" />
                  </label>
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">영업이익률 최소 (%)</div>
                    <input bind:value={experiment.operatingMarginMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 8" />
                  </label>
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">매출 성장 최소</div>
                    <input bind:value={experiment.revenueGrowthMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 0.1" />
                  </label>
                  <label class="text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">순이익 성장 최소</div>
                    <input bind:value={experiment.netIncomeGrowthMin} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" disabled={!factorLab.enabled} placeholder="예: 0.15" />
                  </label>
                </div>

                <div class="mt-3 grid grid-cols-1 gap-3 xl:grid-cols-2">
                  <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
                    <input type="checkbox" bind:checked={experiment.positiveEarningsOnly} disabled={!factorLab.enabled} />
                    흑자 기업만
                  </label>
                  <label class="flex items-center gap-2 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
                    <input type="checkbox" bind:checked={experiment.turnaroundOnly} disabled={!factorLab.enabled} />
                    턴어라운드만
                  </label>
                </div>
              </div>
            {/each}
          </div>
        </div>

        {#if factorLabSummaries.length > 0}
          <div class="mt-4 overflow-auto rounded-xl border border-gray-800 bg-gray-950">
            <table class="min-w-full text-sm">
              <thead class="text-left text-gray-500">
                <tr>
                  <th class="px-3 py-2">프리셋</th>
                  <th class="px-3 py-2">소스</th>
                  <th class="px-3 py-2">매칭 종목</th>
                  <th class="px-3 py-2">실행 여부</th>
                  <th class="px-3 py-2">평균 PER</th>
                  <th class="px-3 py-2">평균 PBR</th>
                  <th class="px-3 py-2">평균 ROE</th>
                  <th class="px-3 py-2">흑자 수</th>
                  <th class="px-3 py-2">턴어라운드 수</th>
                  <th class="px-3 py-2">요약</th>
                </tr>
              </thead>
              <tbody>
                {#each factorLabSummaries as summary}
                  <tr class="border-t border-gray-800">
                    <td class="px-3 py-2">
                      <div class="font-medium text-white">{summary.label}</div>
                      <div class="text-xs text-gray-500">{summary.note}</div>
                    </td>
                    <td class="px-3 py-2 text-gray-300">{factorSourceLabel(summary.source)}</td>
                    <td class="px-3 py-2 text-gray-300">{summary.matched}</td>
                    <td class={`px-3 py-2 ${summary.eligible ? 'text-emerald-300' : 'text-amber-300'}`}>{summary.eligible ? '실행' : '제외'}</td>
                    <td class="px-3 py-2 text-gray-300">{formatDecimal(summary.filteredSummary?.averagePe)}</td>
                    <td class="px-3 py-2 text-gray-300">{formatDecimal(summary.filteredSummary?.averagePb)}</td>
                    <td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.averageRoe != null ? `${formatDecimal(summary.filteredSummary.averageRoe)}%` : '-'}</td>
                    <td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.positiveEarningsCount ?? 0}</td>
                    <td class="px-3 py-2 text-gray-300">{summary.filteredSummary?.turnaroundCount ?? 0}</td>
                    <td class="px-3 py-2 text-gray-400">{summary.summaryTags.join(' · ') || '-'}</td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        {/if}
      </div>

      <div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-2">
        <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
          <div class="mb-3 text-sm font-semibold text-white">비교 구조</div>
          <div class="space-y-3">
            {#each timingStructureOptions as option}
              <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
                <input type="checkbox" checked={timingLab.selectedStructures.includes(option.id)} on:change={() => toggleTimingStructure(option.id)} disabled={!timingLab.enabled} />
                {option.label}
              </label>
            {/each}
          </div>
        </div>

        <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
          <div class="mb-3 text-sm font-semibold text-white">비교 기간 조합</div>
          <div class="grid grid-cols-2 gap-3">
            {#each timingWindowOptions as option}
              <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-950 px-4 py-3 text-sm text-gray-300">
                <input type="checkbox" checked={timingLab.selectedWindows.includes(option.id)} on:change={() => toggleTimingWindow(option.id)} disabled={!timingLab.enabled} />
                {option.label}
              </label>
            {/each}
          </div>
        </div>
      </div>

      <div class="mt-4 rounded-xl border border-amber-700 bg-amber-900/10 p-4 text-sm text-amber-100">
        이 연구실은 선택한 패턴을 저장하지 않고 실행 시점에만 복제해서 타이밍 오버레이를 붙입니다. 빠른 청산 비교를 위해 타이밍 청산은 기존 청산 규칙과 <span class="font-semibold text-white">OR</span>로 합쳐집니다.
      </div>
    </section>

    <section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
      <h3 class="mb-5 text-xl font-semibold">실행 설정</h3>

      <div class="grid grid-cols-1 gap-4 xl:grid-cols-5">
        <label class="text-sm text-gray-300 xl:col-span-2">
          <div class="mb-2 text-gray-500">종목</div>
          <input bind:value={form.symbolsText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="AAPL, MSFT, NVDA" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">시작일</div>
          <input type="date" bind:value={form.from} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">종료일</div>
          <input type="date" bind:value={form.to} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">초기 자본금</div>
          <input type="number" bind:value={form.initialCapital} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      <div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-5">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">타임프레임</div>
          <select bind:value={form.timeFrame} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each timeFrameOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">데이터 소스</div>
          <select bind:value={form.dataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each dataSourceOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">슬리피지 모델</div>
          <select bind:value={form.slippageModel} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each slippageOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">슬리피지 %</div>
          <input type="number" step="0.01" bind:value={form.slippagePercent} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">거래당 수수료</div>
          <input type="number" step="0.1" bind:value={form.commissionPerTrade} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      {#if timeframeWarning()}
        <div class="mt-4 rounded-lg border border-yellow-700 bg-yellow-900/20 p-4 text-sm text-yellow-300">
          <div class="flex items-center gap-2">
            <TriangleAlert size={16} />
            {timeframeWarning()}
          </div>
        </div>
      {/if}

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-2">
        <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
          <div class="mb-4 text-sm font-semibold text-white">고급 분석</div>
          <div class="grid grid-cols-1 gap-4 xl:grid-cols-2">
            <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
              <label class="mb-3 flex items-center gap-2 text-sm text-gray-300">
                <input type="checkbox" bind:checked={form.enableWalkForward} />
                워크포워드 분석
              </label>
              <div class="grid grid-cols-2 gap-2">
                <input type="number" bind:value={form.walkForwardInSampleMonths} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="IS 개월" disabled={!form.enableWalkForward} />
                <input type="number" bind:value={form.walkForwardOutOfSampleMonths} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="OOS 개월" disabled={!form.enableWalkForward} />
              </div>
            </div>
            <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
              <label class="mb-3 flex items-center gap-2 text-sm text-gray-300">
                <input type="checkbox" bind:checked={form.enableMonteCarlo} />
                몬테카를로 시뮬레이션
              </label>
              <input type="number" bind:value={form.monteCarloSimulations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="시뮬레이션 횟수" disabled={!form.enableMonteCarlo} />
            </div>
          </div>
        </div>

        <div class="rounded-xl border border-gray-800 bg-gray-900 p-5">
          <div class="mb-4 text-sm font-semibold text-white">리스크 관리</div>
          <div class="grid grid-cols-2 gap-3 text-sm">
            <label class="text-gray-300">
              <div class="mb-2 text-gray-500">거래당 리스크</div>
              <input type="number" step="0.001" bind:value={form.riskPerTradePercent} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
            </label>
            <label class="text-gray-300">
              <div class="mb-2 text-gray-500">일일 손실 한도</div>
              <input type="number" step="0.005" bind:value={form.dailyLossLimitPercent} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
            </label>
            <label class="text-gray-300">
              <div class="mb-2 text-gray-500">전체 최대 포지션</div>
              <input type="number" bind:value={form.maxTotalPositions} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
            </label>
            <label class="text-gray-300">
              <div class="mb-2 text-gray-500">섹터당 최대 포지션</div>
              <input type="number" bind:value={form.maxPositionsPerSector} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
            </label>
          </div>
        </div>
      </div>

      <div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
        <div class="mb-4 flex items-center justify-between">
          <div class="text-sm font-semibold text-white">포트폴리오 비중 전략</div>
          <label class="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" bind:checked={form.useWeightStrategy} />
            사용
          </label>
        </div>
        <div class="grid grid-cols-2 gap-3 xl:grid-cols-6">
          <input type="number" step="0.05" bind:value={form.bullWeight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="강세 가중치" disabled={!form.useWeightStrategy} />
          <input type="number" step="0.05" bind:value={form.bearWeight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="약세 가중치" disabled={!form.useWeightStrategy} />
          <input type="number" step="0.05" bind:value={form.overheat1Weight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열1 가중치" disabled={!form.useWeightStrategy} />
          <input type="number" step="0.05" bind:value={form.overheat2Weight} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열2 가중치" disabled={!form.useWeightStrategy} />
          <input type="number" step="0.01" bind:value={form.overheatStage1Pct} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열1 임계" disabled={!form.useWeightStrategy} />
          <input type="number" step="0.01" bind:value={form.overheatStage2Pct} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="과열2 임계" disabled={!form.useWeightStrategy} />
        </div>
        <div class="mt-3 max-w-xs">
          <input type="number" bind:value={form.smaPeriod} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white disabled:opacity-40" placeholder="SMA 기간" disabled={!form.useWeightStrategy} />
        </div>
      </div>

      <div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
        <div class="mb-4 text-sm font-semibold text-white">패턴 선택</div>
        {#if loading}
          <div class="text-sm text-gray-400">패턴을 불러오는 중...</div>
        {:else if patterns.length === 0}
          <div class="text-sm text-gray-400">저장된 커스텀 패턴이 없습니다.</div>
        {:else}
          <div class="grid grid-cols-1 gap-3 xl:grid-cols-3">
            {#each patterns as pattern}
              <label class={`rounded-lg border p-4 text-sm transition ${selectedPatternIds.includes(String(pattern.id)) ? 'border-blue-500 bg-blue-950/20' : 'border-gray-800 bg-gray-950 hover:border-gray-700'}`}>
                <div class="flex items-start gap-3">
                  <input type="checkbox" checked={selectedPatternIds.includes(String(pattern.id))} on:change={() => togglePattern(pattern.id)} class="mt-1" />
                  <div>
                    <div class="font-medium text-white">{pattern.name}</div>
                    <div class="mt-1 text-xs text-gray-400">{pattern.description || '설명 없음'}</div>
                  </div>
                </div>
              </label>
            {/each}
          </div>
        {/if}
      </div>

      <div class="mt-6 flex justify-end">
        <button on:click={runBacktest} disabled={running || loading} class="flex items-center gap-2 rounded bg-green-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-green-700 disabled:opacity-50">
          <Play size={16} />
          {running ? (runStatus || '실행 중...') : '백테스트 실행'}
        </button>
      </div>
    </section>

    {#if comparisonResults.length > 0}
      {#if getUniverseComparisonRows().length > 1}
        <section class="rounded-2xl border border-emerald-800/40 bg-emerald-950/10 p-5">
          <div class="text-xl font-semibold text-white">필터 전/후 기준 비교</div>
          <div class="mt-2 text-sm text-gray-400">각 유니버스의 기본 패턴 결과만 따로 뽑아 현재 입력 대비 얼마나 줄었는지 먼저 확인합니다.</div>
          <div class="mt-4 overflow-auto">
            <table class="min-w-full text-sm">
              <thead class="text-left text-gray-500">
                <tr>
                  <th class="px-3 py-2">유니버스</th>
                  <th class="px-3 py-2">종목 수</th>
                  <th class="px-3 py-2">종목 감소</th>
                  <th class="px-3 py-2">총 수익률</th>
                  <th class="px-3 py-2">최대 낙폭</th>
                  <th class="px-3 py-2">샤프</th>
                  <th class="px-3 py-2">거래 수</th>
                </tr>
              </thead>
              <tbody>
                {#each getUniverseComparisonRows() as row}
                  <tr class="border-t border-gray-800">
                    <td class="px-3 py-2 font-medium text-white">{row.label}</td>
                    <td class="px-3 py-2 text-gray-300">{row.symbolCount}</td>
                    <td class={`px-3 py-2 ${row.symbolReduction >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{row.symbolReduction > 0 ? '+' : ''}{row.symbolReduction}</td>
                    <td class={`px-3 py-2 ${Number(row.totalReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(row.totalReturn)}</td>
                    <td class="px-3 py-2 text-red-300">{formatPercent(row.maxDrawdown)}</td>
                    <td class="px-3 py-2 text-gray-300">{Number(row.sharpeRatio ?? 0).toFixed(2)}</td>
                    <td class="px-3 py-2 text-gray-300">{row.totalTrades ?? 0}</td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </section>
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

      <section class="rounded-2xl border border-gray-800 bg-gray-950 p-5">
        <div class="flex items-center justify-between gap-4">
          <div>
            <div class="text-xl font-semibold text-white">타이밍·팩터 비교 결과</div>
            <div class="mt-2 text-sm text-gray-400">행을 클릭하면 아래 상세 결과가 해당 시나리오와 유니버스로 바뀝니다.</div>
          </div>
          {#if runStatus}
            <div class="rounded bg-blue-950/40 px-3 py-2 text-sm text-blue-200">{runStatus}</div>
          {/if}
        </div>

        <div class="mt-4 overflow-auto">
          <table class="min-w-full text-sm">
            <thead class="text-left text-gray-500">
              <tr>
                <th class="px-3 py-2">유니버스</th>
                <th class="px-3 py-2">종목 수</th>
                <th class="px-3 py-2">시나리오</th>
                <th class="px-3 py-2">총 수익률</th>
                <th class="px-3 py-2">최대 낙폭</th>
                <th class="px-3 py-2">샤프</th>
                <th class="px-3 py-2">거래 수</th>
                <th class="px-3 py-2">낙폭 개선</th>
                <th class="px-3 py-2">거래 감소</th>
                <th class="px-3 py-2">휩소 감소</th>
                <th class="px-3 py-2">곡선 안정</th>
              </tr>
            </thead>
            <tbody>
              {#each comparisonResults as entry}
                {@const delta = getComparisonDelta(entry)}
                <tr class={`cursor-pointer border-t border-gray-800 transition ${activeScenarioKey === entry.key ? 'bg-blue-950/20' : 'hover:bg-gray-900'}`} on:click={() => selectScenarioResult(entry.key)}>
                  <td class="px-3 py-2">
                    <div class="font-medium text-white">{entry.comparisonGroupLabel}</div>
                    {#if entry.isBaseline}
                      <div class="text-xs text-emerald-300">이 유니버스의 기준선</div>
                    {/if}
                  </td>
                  <td class="px-3 py-2 text-gray-300">{entry.symbolCount}</td>
                  <td class="px-3 py-2">
                    <div class="font-medium text-white">{entry.label}</div>
                    {#if entry.isBaseline}
                      <div class="text-xs text-blue-300">기본 패턴 시나리오</div>
                    {/if}
                  </td>
                  <td class={`px-3 py-2 ${Number(entry.data.totalReturn ?? 0) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{formatPercent(entry.data.totalReturn)}</td>
                  <td class="px-3 py-2 text-red-300">{formatPercent(entry.data.maxDrawdown)}</td>
                  <td class="px-3 py-2">{Number(entry.data.sharpeRatio ?? 0).toFixed(2)}</td>
                  <td class="px-3 py-2">{entry.data.totalTrades ?? 0}</td>
                  <td class={`px-3 py-2 ${delta && Number(delta.drawdownImprovement) >= 0 ? 'text-green-300' : 'text-red-300'}`}>{delta ? formatSignedPercent(delta.drawdownImprovement) : '-'}</td>
                  <td class={`px-3 py-2 ${delta && Number(delta.tradeReduction) >= 0 ? 'text-blue-200' : 'text-red-300'}`}>{delta ? `${delta.tradeReduction > 0 ? '+' : ''}${delta.tradeReduction}` : '-'}</td>
                  <td class={`px-3 py-2 ${delta && Number(delta.whipsawReduction) >= 0 ? 'text-emerald-300' : 'text-red-300'}`}>{delta ? `${delta.whipsawReduction > 0 ? '+' : ''}${delta.whipsawReduction}` : '-'}</td>
                  <td class={`px-3 py-2 ${delta && Number(delta.stabilityImprovement ?? -1) >= 0 ? 'text-cyan-300' : 'text-red-300'}`}>{delta && delta.stabilityImprovement != null ? formatSignedPercent(delta.stabilityImprovement) : '-'}</td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      </section>
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

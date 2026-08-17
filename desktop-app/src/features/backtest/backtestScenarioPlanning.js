import { formatDecimal, intersectSymbols, uniqueSymbols } from './backtestResearch.js'

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
  return { indicator: 'PRICE_VS_SMA', params: { period }, operator, value: 0, withinBars: 0, refSymbol, compareIndicator: '', compareParams: {}, weight: 1, consecutiveBars: 0 }
}

function normalizeEntryGroups(rawPattern) {
  const entryGroups = safeParseJson(rawPattern.entryGroupsJson, [])
  if (entryGroups.length > 0) return cloneDeep(entryGroups)
  const flatRules = safeParseJson(rawPattern.entryRulesJson, [])
  if (flatRules.length > 0) return [{ label: '기존 진입 규칙', logic: rawPattern.entryLogic ?? 'AND', rules: cloneDeep(flatRules) }]
  return [{ label: '기본 진입 그룹', logic: 'AND', rules: [] }]
}

export function buildScenarioPatterns(basePatterns, scenario, marketSymbolInput = 'SPY') {
  if (scenario.type === 'base') return basePatterns.map((pattern) => cloneDeep(pattern.raw))
  const marketSymbol = (marketSymbolInput || 'SPY').trim().toUpperCase() || 'SPY'
  return basePatterns.map((pattern) => {
    const raw = cloneDeep(pattern.raw)
    const entryGroups = normalizeEntryGroups(raw)
    const exitRules = cloneDeep(safeParseJson(raw.exitRulesJson, []))
    for (const group of entryGroups) {
      group.rules = group.rules ?? []
      group.rules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '>', refSymbol: marketSymbol }))
      if (scenario.structure === 'market-stock') group.rules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '>' }))
    }
    exitRules.push(buildTimingRule({ period: scenario.exitPeriod, operator: '<', refSymbol: marketSymbol }))
    if (scenario.structure === 'market-stock') exitRules.push(buildTimingRule({ period: scenario.entryPeriod, operator: '<' }))
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

export function buildTimingScenarios(timingLab, structureOptions, windowOptions) {
  const scenarios = []
  if (timingLab.includeBaseScenario) scenarios.push({ key: 'base', type: 'base', label: '기본 패턴 그대로', description: '현재 선택한 패턴을 수정 없이 그대로 실행합니다.' })
  for (const structure of timingLab.selectedStructures) {
    const structureLabel = structureOptions.find((item) => item.id === structure)?.label ?? structure
    for (const windowId of timingLab.selectedWindows) {
      const windowConfig = windowOptions.find((item) => item.id === windowId)
      if (!windowConfig) continue
      scenarios.push({ key: `${structure}-${windowConfig.id}`, type: 'overlay', structure, windowId: windowConfig.id, entryPeriod: windowConfig.entryPeriod, exitPeriod: windowConfig.exitPeriod, label: `${structureLabel} ${windowConfig.label}`, description: `${structureLabel}에 ${windowConfig.label} 기간을 적용합니다.` })
    }
  }
  return scenarios
}

export function buildFactorSummaryTags(filteredSummary) {
  const tags = []
  if (filteredSummary?.averagePe != null) tags.push(`평균 PER ${formatDecimal(filteredSummary.averagePe)}`)
  if (filteredSummary?.averagePb != null) tags.push(`평균 PBR ${formatDecimal(filteredSummary.averagePb)}`)
  if (filteredSummary?.averageRoe != null) tags.push(`평균 ROE ${formatDecimal(filteredSummary.averageRoe)}%`)
  if (filteredSummary?.turnaroundCount) tags.push(`턴어라운드 ${filteredSummary.turnaroundCount}개`)
  return tags.slice(0, 3)
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

export function buildFactorExperimentDefinitions(factorLab, presets, financialFactorFilters) {
  const definitions = []
  const seen = new Set()
  const addDefinition = (definition) => {
    const params = normalizeFactorExperimentParams(definition?.params ?? {})
    if (Object.keys(params).length === 0) return
    const signature = JSON.stringify(params)
    if (seen.has(signature)) return
    seen.add(signature)
    definitions.push({ ...definition, params })
  }
  for (const presetId of factorLab.selectedPresets) {
    const preset = presets.find((item) => item.id === presetId)
    if (preset) addDefinition({ id: preset.id, label: preset.label, note: preset.note, source: 'preset', params: preset.params })
  }
  if (factorLab.includeCurrentBuilder) addDefinition({ id: 'current-builder', label: '현재 재무 팩터 빌더', note: '재무 팩터 빌더에서 지금 잡아둔 조건 그대로', source: 'current-builder', params: financialFactorFilters ?? {} })
  for (const experiment of factorLab.customExperiments) {
    addDefinition({ id: experiment.id, label: experiment.label?.trim() || '커스텀 조합', note: '사용자 정의 재무 팩터 조합', source: 'custom', params: { peRatioMax: experiment.peRatioMax, pbRatioMax: experiment.pbRatioMax, roePercentMin: experiment.roePercentMin, operatingMarginMin: experiment.operatingMarginMin, revenueGrowthMin: experiment.revenueGrowthMin, netIncomeGrowthMin: experiment.netIncomeGrowthMin, positiveEarningsOnly: experiment.positiveEarningsOnly, turnaroundOnly: experiment.turnaroundOnly } })
  }
  return definitions
}

export function factorScenarioScore(entry, rankingMode) {
  const totalReturn = Number(entry?.data?.totalReturn ?? 0)
  const sharpeRatio = Number(entry?.data?.sharpeRatio ?? 0)
  const maxDrawdown = Number(entry?.data?.maxDrawdown ?? 0)
  const totalTrades = Number(entry?.data?.totalTrades ?? 0)
  if (rankingMode === 'best-return') return (totalReturn * 140) + (sharpeRatio * 12) - (maxDrawdown * 35) + Math.min(totalTrades, 80) * 0.04
  if (rankingMode === 'best-sharpe') return (sharpeRatio * 120) + (totalReturn * 55) - (maxDrawdown * 45) + Math.min(totalTrades, 80) * 0.05
  if (rankingMode === 'defensive') return (totalReturn * 60) + (sharpeRatio * 40) - (maxDrawdown * 95) + Math.min(totalTrades, 80) * 0.03
  return (totalReturn * 95) + (sharpeRatio * 32) - (maxDrawdown * 60) + Math.min(totalTrades, 80) * 0.04
}

export function buildUniverseVariants({ baseSymbols, extraVariants = [], universeComparison, universeBuilderSymbols, financialFactorSymbols }) {
  const normalizedBaseSymbols = uniqueSymbols(baseSymbols)
  const variants = []
  const seen = new Set()
  const addVariant = (variant) => {
    if (!variant?.symbols?.length) return
    const symbols = uniqueSymbols(variant.symbols)
    const signature = [...symbols].sort().join('|')
    if (!signature || seen.has(signature)) return
    seen.add(signature)
    variants.push({ ...variant, symbols, symbolCount: symbols.length })
  }
  if (universeComparison.includeCurrentSymbols) addVariant({ key: 'current', label: '현재 입력', description: '현재 종목 입력을 그대로 실행합니다.', symbols: normalizedBaseSymbols })
  if (universeComparison.enabled && universeComparison.includeUniverseBuilder) {
    const filtered = intersectSymbols(normalizedBaseSymbols, universeBuilderSymbols)
    addVariant({ key: 'universe', label: '시총·섹터 필터', description: `${normalizedBaseSymbols.length}개 중 ${filtered.length}개가 유니버스 빌더 조건에 남았습니다.`, symbols: filtered })
  }
  if (universeComparison.enabled && universeComparison.includeFinancialFactor) {
    const filtered = intersectSymbols(normalizedBaseSymbols, financialFactorSymbols)
    addVariant({ key: 'financial', label: '재무 팩터 필터', description: `${normalizedBaseSymbols.length}개 중 ${filtered.length}개가 재무 팩터 조건에 남았습니다.`, symbols: filtered })
  }
  if (universeComparison.enabled && universeComparison.includeCombined) addVariant({ key: 'combined', label: '교집합 필터', description: '시총·섹터·재무 팩터 조건을 모두 만족한 종목만 실행합니다.', symbols: intersectSymbols(intersectSymbols(normalizedBaseSymbols, universeBuilderSymbols), financialFactorSymbols) })
  for (const variant of extraVariants) addVariant(variant)
  return variants
}

export function combineScenarioPlans(universeVariants, timingScenarios) {
  return universeVariants.flatMap((variant) => timingScenarios.map((scenario) => ({ ...scenario, key: `${variant.key}::${scenario.key}`, label: `${variant.label} · ${scenario.label}`, description: `${variant.description} ${scenario.description}`, comparisonGroupKey: variant.key, comparisonGroupLabel: variant.label, comparisonGroupKind: variant.kind ?? 'standard', symbols: variant.symbols, symbolCount: variant.symbolCount, factorPresetId: variant.factorPresetId ?? null, factorPresetLabel: variant.factorPresetLabel ?? null, factorPresetNote: variant.factorPresetNote ?? null })))
}

import { uniqueSymbols } from './backtestResearch.js'

export function createBacktestForm(defaultSlippageModel = '') {
  return {
    symbolsText: 'SPY, QQQ, TQQQ',
    from: '',
    to: '',
    initialCapital: 100000,
    timeFrame: 'Daily',
    dataSource: '',
    slippageModel: defaultSlippageModel,
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
}

export function projectBacktestMetadata(metadata) {
  const timeFrameOptions = (metadata?.timeFrames ?? []).map((item) => [item.value, item.displayName])
  const dataProviders = metadata?.dataProviders ?? []
  const slippageModels = metadata?.slippageModels ?? []
  const defaultSlippageModel = slippageModels.find((item) => item.isDefault)?.value

  if (!timeFrameOptions.length || !dataProviders.length || !slippageModels.length || !defaultSlippageModel) {
    throw new Error('서버의 백테스트 실행 메타데이터가 비어 있습니다.')
  }

  return {
    timeFrameOptions,
    dataProviders,
    dataSourceOptions: [['', '기본 설정'], ...dataProviders.map((item) => [item.value, item.displayName])],
    slippageOptions: slippageModels.map((item) => [item.value, item.displayName, item.description]),
    defaultSlippageModel
  }
}

export function createTimingLab() {
  return {
    enabled: true,
    includeBaseScenario: true,
    marketSymbol: 'SPY',
    selectedStructures: ['market', 'market-stock'],
    selectedWindows: ['20-20', '20-10']
  }
}

export function createUniverseComparison() {
  return {
    enabled: true,
    includeCurrentSymbols: true,
    includeUniverseBuilder: true,
    includeFinancialFactor: true,
    includeCombined: true
  }
}

export function createCustomFactorExperiment(ordinal, id = `custom-${ordinal}`) {
  return {
    id,
    label: `커스텀 조합 ${ordinal}`,
    peRatioMax: '',
    pbRatioMax: '',
    roePercentMin: '',
    operatingMarginMin: '',
    revenueGrowthMin: '',
    netIncomeGrowthMin: '',
    positiveEarningsOnly: true,
    turnaroundOnly: false
  }
}

export function createFactorLab() {
  return {
    enabled: false,
    selectedPresets: ['value-pe', 'quality-roe', 'turnaround-growth'],
    includeCurrentBuilder: true,
    minMatchedSymbols: 2,
    rankingMode: 'balanced',
    topRankedResults: 5,
    customExperiments: [createCustomFactorExperiment(1, 'custom-1')]
  }
}

export function toggleSelection(items, value) {
  return items.includes(value) ? items.filter((item) => item !== value) : [...items, value]
}

export function parseBacktestSymbols(symbolsText) {
  return (symbolsText ?? '').split(',').map((item) => item.trim().toUpperCase()).filter(Boolean)
}

export function backtestSymbolSignature(symbols) {
  return uniqueSymbols(symbols).join('|')
}

export function buildTimeframeWarning(form, dataProviders, timeFrameOptions) {
  if (!form.from || !form.to) return ''
  const provider = dataProviders.find((item) => item.value === form.dataSource)
  const maxDays = provider?.maximumLookbackDays?.[form.timeFrame]
  const days = Math.max(0, Math.ceil((new Date(form.to).getTime() - new Date(form.from).getTime()) / 86400000))
  if (!maxDays || days <= maxDays) return ''
  const frameLabel = timeFrameOptions.find(([value]) => value === form.timeFrame)?.[1] ?? form.timeFrame
  return `${provider.displayName}의 ${frameLabel} 조회 한도는 최대 ${maxDays}일입니다.`
}

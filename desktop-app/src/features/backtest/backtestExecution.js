import { buildScenarioPatterns } from './backtestScenarioPlanning.js'
import { toStrategyDocument } from '../strategies/strategyDocument.js'

export function buildBacktestRequestPayload(form, symbols, customPatternRaws) {
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
    customPatterns: customPatternRaws.map(toStrategyDocument)
  }
}

export function decorateScenarioResult(responseData, symbols, basePatterns, scenario) {
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
      ...responseData,
      request: {
        symbols,
        patternNames: basePatterns.map((pattern) => pattern.name),
        universeVariant: { key: scenario.comparisonGroupKey ?? 'current', label: scenario.comparisonGroupLabel ?? '현재 입력', symbolCount: scenario.symbolCount ?? symbols.length, kind: scenario.comparisonGroupKind ?? 'standard', factorPresetLabel: scenario.factorPresetLabel ?? null }
      },
      timingScenario: { key: scenario.key, label: scenario.label, description: scenario.description }
    }
  }
}

export async function runBacktestScenarios({ startBacktest, form, scenarios, basePatterns, marketSymbol, onProgress = () => {} }) {
  const results = []
  for (let index = 0; index < scenarios.length; index += 1) {
    const scenario = scenarios[index]
    onProgress({ current: index + 1, total: scenarios.length, scenario })
    const scenarioPatterns = buildScenarioPatterns(basePatterns, scenario, marketSymbol)
    const response = await startBacktest(buildBacktestRequestPayload(form, scenario.symbols, scenarioPatterns))
    results.push(decorateScenarioResult(response.data, scenario.symbols, basePatterns, scenario))
  }
  return results
}

export async function runPlainBacktest({ startBacktest, form, symbols, basePatterns }) {
  const response = await startBacktest(buildBacktestRequestPayload(form, symbols, basePatterns.map((pattern) => pattern.raw)))
  return { ...response.data, request: { symbols, patternNames: basePatterns.map((pattern) => pattern.name) } }
}

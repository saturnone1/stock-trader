import {
  buildFactorExperimentDefinitions,
  buildTimingScenarios,
  buildUniverseVariants,
  combineScenarioPlans
} from './backtestScenarioPlanning.js'
import {
  buildFactorLabInsightCards,
  buildFactorLabRankingRows,
  buildFactorLabSummaryLine,
  buildScenarioComparisonRows,
  buildTimingReport,
  buildUniverseComparisonRows
} from './backtestResultAnalysis.js'
import {
  factorExperimentPresets,
  factorRankingOptions,
  intersectSymbols,
  timingStructureOptions,
  timingWindowOptions,
  uniqueSymbols
} from './backtestResearch.js'
import {
  backtestSymbolSignature,
  buildTimeframeWarning,
  parseBacktestSymbols
} from './backtestWorkspace.js'

function researchPlans(state, symbols, extraVariants) {
  const universeVariants = buildUniverseVariants({
    baseSymbols: symbols,
    extraVariants,
    universeComparison: state.universeComparison,
    universeBuilderSymbols: state.universeBuilderSymbols,
    financialFactorSymbols: state.financialFactorSymbols
  })
  const timingScenarios = buildTimingScenarios(
    state.timingLab,
    timingStructureOptions,
    timingWindowOptions
  )
  return { universeVariants, scenarioPlans: combineScenarioPlans(universeVariants, timingScenarios) }
}

export function buildBacktestResearchPlans(state, symbols, extraVariants = []) {
  return researchPlans(state, symbols, extraVariants)
}

export function buildBacktestViewModel(state) {
  const symbols = parseBacktestSymbols(state.form.symbolsText)
  const selectedPatterns = state.patterns.filter((pattern) =>
    state.selectedPatternIds.includes(String(pattern.id)))
  const factorDefinitions = buildFactorExperimentDefinitions(
    state.factorLab,
    factorExperimentPresets,
    state.financialFactorFilters
  )
  const currentSignature = backtestSymbolSignature(symbols)
  const cachedFactorVariants = state.factorLab.enabled && state.factorLabBaseSignature === currentSignature
    ? state.factorLabVariants
    : []
  const plans = researchPlans(state, symbols, cachedFactorVariants)
  const factorRankingRows = buildFactorLabRankingRows(
    state.comparisonResults,
    state.factorLabSummaries,
    state.factorLab.rankingMode,
    state.factorLab.topRankedResults
  )
  const activeComparisonEntry = state.comparisonResults.find(
    (item) => item.key === state.activeScenarioKey) ?? null

  return {
    symbols,
    selectedPatterns,
    factorDefinitions,
    factorExperimentSelectionCount: factorDefinitions.length,
    factorRankingLabel: factorRankingOptions.find(
      (option) => option.id === state.factorLab.rankingMode)?.label ?? '균형 점수',
    estimatedScenarioCount: state.timingLab.enabled ? plans.scenarioPlans.length : 1,
    currentSymbolCount: uniqueSymbols(symbols).length,
    universeSymbolCount: intersectSymbols(symbols, state.universeBuilderSymbols).length,
    financialSymbolCount: intersectSymbols(symbols, state.financialFactorSymbols).length,
    combinedSymbolCount: intersectSymbols(
      intersectSymbols(symbols, state.universeBuilderSymbols),
      state.financialFactorSymbols
    ).length,
    timeframeWarning: buildTimeframeWarning(state.form, state.dataProviders, state.timeFrameOptions),
    activeComparisonEntry,
    timingReport: buildTimingReport(state.comparisonResults, activeComparisonEntry),
    universeComparisonRows: buildUniverseComparisonRows(state.comparisonResults),
    factorRankingRows,
    factorLabInsightCards: buildFactorLabInsightCards(factorRankingRows),
    factorLabSummaryLine: buildFactorLabSummaryLine(factorRankingRows),
    scenarioComparisonRows: buildScenarioComparisonRows(state.comparisonResults)
  }
}

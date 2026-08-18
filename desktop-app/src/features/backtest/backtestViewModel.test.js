import test from 'node:test'
import assert from 'node:assert/strict'
import { buildBacktestResearchPlans, buildBacktestViewModel } from './backtestViewModel.js'
import { createBacktestForm, createFactorLab, createTimingLab, createUniverseComparison } from './backtestWorkspace.js'

function state() {
  return {
    form: { ...createBacktestForm(), symbolsText: 'SPY, QQQ' },
    patterns: [{ id: 1, name: '선택' }, { id: 2, name: '제외' }],
    selectedPatternIds: ['1'],
    timingLab: createTimingLab(),
    universeComparison: createUniverseComparison(),
    universeBuilderSymbols: ['SPY'],
    financialFactorSymbols: ['SPY', 'QQQ'],
    financialFactorFilters: null,
    factorLab: createFactorLab(),
    factorLabVariants: [],
    factorLabBaseSignature: '',
    factorLabSummaries: [],
    comparisonResults: [],
    activeScenarioKey: '',
    dataProviders: [],
    timeFrameOptions: []
  }
}

test('backtest view model projects one consistent research selection', () => {
  const source = state()
  const result = buildBacktestViewModel(source)

  assert.deepEqual(result.symbols, ['SPY', 'QQQ'])
  assert.deepEqual(result.selectedPatterns, [{ id: 1, name: '선택' }])
  assert.equal(result.factorExperimentSelectionCount, 4)
  assert.equal(result.factorRankingLabel, '균형 점수')
  assert.equal(result.currentSymbolCount, 2)
  assert.equal(result.universeSymbolCount, 1)
  assert.equal(result.combinedSymbolCount, 1)
  assert.equal(result.estimatedScenarioCount, 10)
})

test('stale factor variants never affect scenario estimates after symbols change', () => {
  const source = state()
  source.factorLab.enabled = true
  source.factorLabVariants = [{ key: 'stale', label: '오래된 캐시', symbols: ['QQQ'] }]
  source.factorLabBaseSignature = 'AAPL'

  const stale = buildBacktestViewModel(source)
  source.factorLabBaseSignature = 'SPY|QQQ'
  const current = buildBacktestViewModel(source)

  assert.equal(stale.estimatedScenarioCount, 10)
  assert.equal(current.estimatedScenarioCount, 15)
})

test('run planning returns universe and timing plans from the same inputs', () => {
  const source = state()
  const plans = buildBacktestResearchPlans(source, ['SPY', 'QQQ'], [{
    key: 'factor', label: '팩터', symbols: ['QQQ']
  }])

  assert.deepEqual(plans.universeVariants.map((item) => item.key), ['current', 'universe', 'factor'])
  assert.equal(plans.scenarioPlans.length, 15)
})

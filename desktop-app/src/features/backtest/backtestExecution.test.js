import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildBacktestRequestPayload,
  decorateScenarioResult,
  runBacktestScenarios,
  runPlainBacktest
} from './backtestExecution.js'

function createForm(overrides = {}) {
  return {
    from: '2024-01-01',
    to: '2024-12-31',
    initialCapital: '100000',
    timeFrame: 'Daily',
    slippagePercent: '0.05',
    commissionPerTrade: '1',
    slippageModel: 'Adaptive',
    enableWalkForward: true,
    walkForwardInSampleMonths: '12',
    walkForwardOutOfSampleMonths: '3',
    enableMonteCarlo: false,
    monteCarloSimulations: '1000',
    riskPerTradePercent: '1',
    dailyLossLimitPercent: '3',
    maxTotalPositions: '8',
    maxPositionsPerSector: '3',
    dataSource: '',
    useWeightStrategy: false,
    bullWeight: '1.2',
    bearWeight: '0.6',
    overheat1Weight: '0.8',
    overheat2Weight: '0.4',
    overheatStage1Pct: '8',
    overheatStage2Pct: '15',
    smaPeriod: '200',
    ...overrides
  }
}

test('request payload preserves the API contract and normalizes form values', () => {
  const patterns = [{ name: '반등' }]
  const payload = buildBacktestRequestPayload(createForm(), ['TQQQ'], patterns)

  assert.deepEqual(payload, {
    symbols: ['TQQQ'],
    patterns: ['Custom'],
    from: '2024-01-01',
    to: '2024-12-31',
    initialCapital: 100000,
    timeFrame: 'Daily',
    slippagePercent: 0.05,
    commissionPerTrade: 1,
    slippageModel: 'Adaptive',
    enableWalkForward: true,
    walkForwardInSampleMonths: 12,
    walkForwardOutOfSampleMonths: 3,
    enableMonteCarlo: false,
    monteCarloSimulations: 1000,
    riskPerTradePercent: 1,
    dailyLossLimitPercent: 3,
    maxTotalPositions: 8,
    maxPositionsPerSector: 3,
    dataSource: null,
    weightStrategy: null,
    backtestMode: 'pattern',
    customPatterns: patterns
  })
})

test('request payload includes the configured portfolio weight strategy only when enabled', () => {
  const payload = buildBacktestRequestPayload(createForm({ useWeightStrategy: true, dataSource: 'Alpaca' }), ['SPY'], [])

  assert.equal(payload.dataSource, 'Alpaca')
  assert.deepEqual(payload.weightStrategy, {
    bullWeight: 1.2,
    bearWeight: 0.6,
    overheat1Weight: 0.8,
    overheat2Weight: 0.4,
    overheatStage1Pct: 8,
    overheatStage2Pct: 15,
    smaPeriod: 200
  })
})

test('scenario results retain baseline, universe, and request metadata', () => {
  const result = decorateScenarioResult(
    { totalReturn: 0.12 },
    ['SPY', 'QQQ'],
    [{ name: '추세 추종' }],
    { key: 'current::base', type: 'base', label: '현재 입력 · 기본', description: '원본 전략', comparisonGroupKey: 'current', comparisonGroupLabel: '현재 입력' })

  assert.equal(result.isBaseline, true)
  assert.equal(result.symbolCount, 2)
  assert.equal(result.data.totalReturn, 0.12)
  assert.deepEqual(result.data.request.symbols, ['SPY', 'QQQ'])
  assert.deepEqual(result.data.request.patternNames, ['추세 추종'])
  assert.equal(result.data.request.universeVariant.label, '현재 입력')
  assert.equal(result.data.timingScenario.key, 'current::base')
})

test('scenario runner executes sequentially, reports progress, and sends timing overlays', async () => {
  const calls = []
  const progress = []
  const basePatterns = [{
    name: '반등',
    raw: { name: '반등', entryRulesJson: '[]', exitRulesJson: '[]', entryLogic: 'AND', requireBullRegime: true }
  }]
  const scenarios = [
    { key: 'base', type: 'base', label: '기본', description: '원본', symbols: ['TQQQ'] },
    { key: 'market-20-10', type: 'overlay', structure: 'market', entryPeriod: 20, exitPeriod: 10, label: '시장 20/10', description: '시장 필터', symbols: ['TQQQ'] }
  ]

  const results = await runBacktestScenarios({
    startBacktest: async (payload) => {
      calls.push(payload)
      return { data: { totalReturn: calls.length / 10 } }
    },
    form: createForm(),
    scenarios,
    basePatterns,
    marketSymbol: 'spy',
    onProgress: (event) => progress.push(`${event.current}/${event.total}:${event.scenario.key}`)
  })

  assert.deepEqual(progress, ['1/2:base', '2/2:market-20-10'])
  assert.equal(calls.length, 2)
  assert.equal(calls[0].customPatterns[0].name, '반등')
  assert.equal(calls[1].customPatterns[0].name, '반등 · 시장 20/10')
  assert.equal(JSON.parse(calls[1].customPatterns[0].entryGroupsJson)[0].rules[0].refSymbol, 'SPY')
  assert.equal(calls[1].customPatterns[0].exitRulesLogic, 'OR')
  assert.equal(results[0].isBaseline, true)
  assert.equal(results[1].data.totalReturn, 0.2)
})

test('plain runner decorates the API result with the submitted symbols and pattern names', async () => {
  let submitted
  const result = await runPlainBacktest({
    startBacktest: async (payload) => {
      submitted = payload
      return { data: { totalTrades: 7 } }
    },
    form: createForm(),
    symbols: ['QQQ'],
    basePatterns: [{ name: '돌파', raw: { name: '돌파' } }]
  })

  assert.equal(submitted.customPatterns[0].name, '돌파')
  assert.equal(result.totalTrades, 7)
  assert.deepEqual(result.request, { symbols: ['QQQ'], patternNames: ['돌파'] })
})

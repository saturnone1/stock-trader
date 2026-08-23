import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildOptimizationJob,
  entryRules,
  estimatedCombinationCount,
  exitRules,
  projectOptimizationRankingMetadata,
  resultInsights
} from './optimizationModel.js'

const form = {
  symbolsText: 'spy, QQQ', from: '2025-01-01', to: '2025-12-31', jobName: '',
  priority: '1', chunkSize: '200', maxDurationHours: '', maxTestedCombinations: '',
  topResultsToKeep: '50', rankBy: 'sortinoRatio', continuousMode: false,
  autoApplyBestResult: false, autoApplyMinTrades: '10', timeFrame: 'Daily', dataSource: '',
  maxResults: '10', maxCombinations: '500', oosPercent: '0.25', timingFocusMode: true,
  selectedEntryRuleIndex: '0', selectedExitRuleIndex: '1', entryPeriodValuesText: '20, 10, 20',
  exitPeriodValuesText: '5, 10', sweepEntryLogic: false, sweepExitLogic: false,
  sweepRequireBullRegime: true, sweepEntryMode: false, sweepSizingMode: false,
  includeRiskExitAxes: false, entryLogicOptions: ['AND', 'OR'], exitLogicOptions: ['OR', 'AND'],
  requireBullRegimeOptions: [true, false], entryModeOptions: ['CurrentClose', 'NextOpen'],
  sizingModeOptions: ['FixedRisk', 'Kelly'], atrStopMin: 1.5, atrStopMax: 3, atrStopStep: .5,
  atrTargetMin: 2, atrTargetMax: 5, atrTargetStep: .5, maxHoldingMin: 5, maxHoldingMax: 20,
  maxHoldingStep: 5, trailingAtrMin: 0, trailingAtrMax: 2, trailingAtrStep: .5,
  partialProfitMin: 0, partialProfitMax: 3, partialProfitStep: .5,
  defaultAllocationMin: 30, defaultAllocationMax: 100, defaultAllocationStep: 10
}

test('optimization job payload preserves timing sweep API contract', () => {
  const { payload, error } = buildOptimizationJob(form, { name: '추세', raw: { name: '추세' } })
  assert.equal(error, undefined)
  assert.equal(payload.name, '추세 타이밍 최적화')
  assert.deepEqual(payload.optimizeRequest.symbols, ['SPY', 'QQQ'])
  assert.deepEqual(payload.optimizeRequest.optimizeParams.ruleParamOverrides, [
    { scope: 'Entry', ruleIndex: 0, paramKey: 'period', values: [10, 20] },
    { scope: 'Exit', ruleIndex: 1, paramKey: 'period', values: [5, 10] }
  ])
  assert.equal(payload.optimizeRequest.optimizeParams.entryLogicOptions, null)
  assert.deepEqual(payload.optimizeRequest.optimizeParams.requireBullRegimeOptions, [true, false])
  assert.equal(payload.optimizeRequest.optimizeParams.atrStopMultiplier, null)
})

test('optimization ranking choices and default come from server metadata', () => {
  const projected = projectOptimizationRankingMetadata({
    optimizationRankings: [
      { code: 'sortinoRatio', displayName: '소르티노 비율', isDefault: true },
      { code: 'annualizedReturn', displayName: '연환산 수익률', isDefault: false }
    ]
  })

  assert.deepEqual(projected.rankOptions, [
    ['sortinoRatio', '소르티노 비율'],
    ['annualizedReturn', '연환산 수익률']
  ])
  assert.equal(projected.defaultRankBy, 'sortinoRatio')
  assert.throws(() => projectOptimizationRankingMetadata({}), /순위 메타데이터가 비어 있습니다/)
})

test('combination estimate multiplies only enabled timing axes', () => {
  assert.equal(estimatedCombinationCount(form), 8)
})

test('tuning reads grouped buy and sell rules with legacy fallbacks', () => {
  const grouped = {
    entryGroupsJson: JSON.stringify([{ rules: [{ indicator: 'BREAKOUT_HIGH', params: { period: 20 } }] }]),
    exitGroupsJson: JSON.stringify([{ Rules: [{ Indicator: 'BREAKOUT_LOW', Params: { period: 10 } }] }]),
    entryRulesJson: JSON.stringify([{ indicator: 'RSI' }]),
    exitRulesJson: JSON.stringify([{ indicator: 'ATR' }])
  }
  assert.equal(entryRules(grouped)[0].indicator, 'BREAKOUT_HIGH')
  assert.equal(exitRules(grouped)[0].Indicator, 'BREAKOUT_LOW')

  assert.equal(entryRules({ entryRulesJson: grouped.entryRulesJson })[0].indicator, 'RSI')
  assert.equal(exitRules({ exitRulesJson: grouped.exitRulesJson })[0].indicator, 'ATR')
})

test('investor-focused tuning changes only one strategy area', () => {
  const focused = { ...form, tuningFocus: 'entry', sweepRequireBullRegime: true, includeRiskExitAxes: true }
  const { payload } = buildOptimizationJob(focused, { name: '추세', raw: { name: '추세' } })

  assert.equal(estimatedCombinationCount(focused), 2)
  assert.deepEqual(payload.optimizeRequest.optimizeParams.ruleParamOverrides, [
    { scope: 'Entry', ruleIndex: 0, paramKey: 'period', values: [10, 20] }
  ])
  assert.equal(payload.optimizeRequest.optimizeParams.atrStopMultiplier, null)
  assert.equal(payload.optimizeRequest.optimizeParams.requireBullRegimeOptions, null)
})

test('risk tuning compares only stop and target ranges', () => {
  const focused = { ...form, tuningFocus: 'risk', timingFocusMode: false, includeRiskExitAxes: true }
  const { payload } = buildOptimizationJob(focused, { name: '추세', raw: { name: '추세' } })
  const params = payload.optimizeRequest.optimizeParams

  assert.deepEqual(params.ruleParamOverrides, [])
  assert.deepEqual(params.atrStopMultiplier, { min: 1.5, max: 3, step: 0.5 })
  assert.deepEqual(params.atrTargetMultiplier, { min: 2, max: 5, step: 0.5 })
  assert.equal(params.maxHoldingBars, null)
  assert.equal(params.defaultAllocationPercent, null)
})

test('multi-result insights format signed median deltas without a runtime error', () => {
  const results = [
    { tradeCount: 10, maxDrawdown: .2, profitFactor: 1.2, totalReturn: .1 },
    { tradeCount: 20, maxDrawdown: .1, profitFactor: 1.5, totalReturn: .3 }
  ]
  const insights = resultInsights(results[1], results)
  assert.equal(insights.length, 4)
  assert.equal(insights[0].value, '+5.00%')
  assert.match(insights[2].value, /^[+-]?\d+\.\d{2}%$/)
})

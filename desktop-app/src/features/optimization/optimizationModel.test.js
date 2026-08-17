import test from 'node:test'
import assert from 'node:assert/strict'
import { buildOptimizationJob, estimatedCombinationCount, resultInsights } from './optimizationModel.js'

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

test('combination estimate multiplies only enabled timing axes', () => {
  assert.equal(estimatedCombinationCount(form), 8)
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

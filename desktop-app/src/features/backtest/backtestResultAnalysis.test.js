import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildFactorLabRankingRows,
  buildScenarioComparisonRows,
  buildTimingReport,
  buildUniverseComparisonRows,
  calculateComparisonDelta
} from './backtestResultAnalysis.js'

function result(overrides = {}) {
  return {
    request: { symbols: ['SPY', 'QQQ', 'TQQQ'] },
    totalReturn: 0.1,
    maxDrawdown: 0.2,
    sharpeRatio: 1,
    totalTrades: 10,
    usedTimeFrame: 'Daily',
    trades: [{ entryTime: '2026-01-02T00:00:00Z', exitTime: '2026-01-05T00:00:00Z', returnPct: -0.01 }],
    equityCurve: [{ equity: 100 }, { equity: 110 }, { equity: 100 }],
    ...overrides
  }
}

test('comparison delta uses the matching group baseline and API trade return contract', () => {
  const baseline = { key: 'current::base', comparisonGroupKey: 'current', comparisonGroupLabel: '현재 입력', isBaseline: true, data: result() }
  const overlay = { key: 'current::overlay', comparisonGroupKey: 'current', comparisonGroupLabel: '현재 입력', isBaseline: false, data: result({ totalReturn: 0.15, maxDrawdown: 0.1, totalTrades: 7, trades: [], equityCurve: [{ equity: 100 }, { equity: 105 }, { equity: 110 }] }) }
  const delta = calculateComparisonDelta([baseline, overlay], overlay)
  const report = buildTimingReport([baseline, overlay], overlay)

  assert.ok(Math.abs(delta.returnDelta - 0.05) < 1e-12)
  assert.equal(delta.drawdownImprovement, 0.1)
  assert.equal(delta.tradeReduction, 3)
  assert.equal(delta.whipsawReduction, 1)
  assert.equal(report.currentWhipsawCount, 0)
  assert.ok(delta.stabilityImprovement > 0)
})

test('universe rows compare each group baseline symbol count against current input', () => {
  const results = [
    { comparisonGroupKey: 'current', comparisonGroupLabel: '현재 입력', isBaseline: true, data: result() },
    { comparisonGroupKey: 'filtered', comparisonGroupLabel: '필터', isBaseline: true, data: result({ request: { symbols: ['SPY'] }, totalReturn: 0.2 }) }
  ]
  const rows = buildUniverseComparisonRows(results)

  assert.equal(rows.length, 2)
  assert.equal(rows[1].symbolReduction, 2)
  assert.equal(rows[1].totalReturn, 0.2)
})

test('factor ranking selects the best scenario within each factor group', () => {
  const base = { comparisonGroupKey: 'factor-value', comparisonGroupKind: 'factor-lab', comparisonGroupLabel: '저PER', factorPresetId: 'value', factorPresetLabel: '저PER', symbolCount: 3, isBaseline: true, label: '기본', data: result() }
  const overlay = { ...base, key: 'overlay', isBaseline: false, label: '20/10', data: result({ totalReturn: 0.25, maxDrawdown: 0.1, sharpeRatio: 1.5, timingScenario: { label: '시장 20/10' } }) }
  const rows = buildFactorLabRankingRows([base, overlay], [{ id: 'value', note: 'PER 15 이하', source: 'preset', summaryTags: ['평균 PER 10'] }], 'balanced', 5)

  assert.equal(rows.length, 1)
  assert.equal(rows[0].rank, 1)
  assert.equal(rows[0].bestScenarioLabel, '시장 20/10')
  assert.equal(rows[0].bestReturn, 0.25)
})

test('scenario rows attach no delta to baseline and group delta to overlays', () => {
  const baseline = { comparisonGroupKey: 'current', isBaseline: true, data: result() }
  const overlay = { comparisonGroupKey: 'current', isBaseline: false, data: result({ totalTrades: 8 }) }
  const rows = buildScenarioComparisonRows([baseline, overlay])

  assert.equal(rows[0].delta, null)
  assert.equal(rows[1].delta.tradeReduction, 2)
})

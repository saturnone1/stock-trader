import test from 'node:test'
import assert from 'node:assert/strict'
import { estimateHoldingBars, formatBacktestTimestamp, formatPercentagePoints, getWhipsawStats, regimeDisplayName } from './backtestResearch.js'

test('daily holding bars count trading days instead of dividing wall-clock minutes by session length', () => {
  assert.equal(estimateHoldingBars({ entryTime: '2026-01-02T00:00:00Z', exitTime: '2026-01-05T00:00:00Z' }, 'Daily'), 1)
  assert.equal(estimateHoldingBars({ entryTime: '2026-01-05T00:00:00Z', exitTime: '2026-01-08T00:00:00Z' }, 'Daily'), 3)
  assert.equal(estimateHoldingBars({ entryTime: '2026-01-05T00:00:00Z', exitTime: '2026-01-12T00:00:00Z' }, 'Weekly'), 1)
})

test('whipsaw classification reads the API returnPct contract', () => {
  const stats = getWhipsawStats({
    usedTimeFrame: 'Daily',
    trades: [
      { entryTime: '2026-01-02T00:00:00Z', exitTime: '2026-01-05T00:00:00Z', returnPct: -0.02 },
      { entryTime: '2026-01-05T00:00:00Z', exitTime: '2026-01-06T00:00:00Z', returnPct: 0.01 }
    ]
  })

  assert.deepEqual(stats, { count: 1, rate: 0.5, thresholdBars: 3 })
})

test('backtest result labels preserve percentage-point units and intraday timestamps', () => {
  const timestamp = '2026-08-18T09:31:00.0000000Z'

  assert.equal(formatPercentagePoints(12.5), '12.50%')
  assert.equal(regimeDisplayName('Bull'), '상승장')
  assert.equal(regimeDisplayName('Bear'), '하락장')
  assert.equal(regimeDisplayName('2026'), '2026')
  assert.notEqual(
    formatBacktestTimestamp(timestamp, 'OneMinute'),
    formatBacktestTimestamp(timestamp, 'Daily')
  )
})

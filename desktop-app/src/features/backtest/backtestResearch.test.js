import test from 'node:test'
import assert from 'node:assert/strict'
import { estimateHoldingBars, getWhipsawStats } from './backtestResearch.js'

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

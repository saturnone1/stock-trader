import test from 'node:test'
import assert from 'node:assert/strict'
import {
  backtestSymbolSignature,
  buildTimeframeWarning,
  createBacktestForm,
  createCustomFactorExperiment,
  createFactorLab,
  createTimingLab,
  parseBacktestSymbols,
  toggleSelection
} from './backtestWorkspace.js'

test('backtest reset factories return independent canonical research state', () => {
  const first = createBacktestForm()
  const second = createBacktestForm()
  first.symbolsText = 'AAPL'

  assert.equal(second.symbolsText, 'SPY, QQQ, TQQQ')
  assert.deepEqual(createTimingLab().selectedWindows, ['20-20', '20-10'])
  assert.deepEqual(createFactorLab().customExperiments, [createCustomFactorExperiment(1, 'custom-1')])
})

test('symbol parsing preserves request order while signatures normalize cache identity', () => {
  const symbols = parseBacktestSymbols(' qqq, SPY, qqq, , tqqq ')

  assert.deepEqual(symbols, ['QQQ', 'SPY', 'QQQ', 'TQQQ'])
  assert.equal(backtestSymbolSignature(symbols), 'QQQ|SPY|TQQQ')
  assert.deepEqual(toggleSelection(['market'], 'market'), [])
  assert.deepEqual(toggleSelection(['market'], 'market-stock'), ['market', 'market-stock'])
})

test('timeframe warning uses the selected provider capability', () => {
  const form = { from: '2026-01-01', to: '2026-02-15', dataSource: 'Alpaca', timeFrame: 'OneMinute' }
  const providers = [{ value: 'Alpaca', displayName: 'Alpaca', maximumLookbackDays: { OneMinute: 30 } }]

  assert.equal(
    buildTimeframeWarning(form, providers, [['OneMinute', '1분봉']]),
    'Alpaca의 1분봉 조회 한도는 최대 30일입니다.'
  )
  assert.equal(buildTimeframeWarning({ ...form, to: '2026-01-15' }, providers, []), '')
})

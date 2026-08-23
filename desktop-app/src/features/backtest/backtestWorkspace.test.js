import test from 'node:test'
import assert from 'node:assert/strict'
import {
  backtestSymbolSignature,
  buildOptimizationContext,
  buildTimeframeWarning,
  createBacktestForm,
  createCustomFactorExperiment,
  createFactorLab,
  createTimingLab,
  parseBacktestSymbols,
  projectBacktestMetadata,
  toggleSelection
} from './backtestWorkspace.js'

test('backtest reset factories return independent canonical research state', () => {
  const first = createBacktestForm()
  const second = createBacktestForm()
  first.symbolsText = 'AAPL'

  assert.equal(second.symbolsText, 'SPY, QQQ, TQQQ')
  assert.equal(createTimingLab().enabled, false)
  assert.deepEqual(createTimingLab().selectedWindows, ['20-20', '20-10'])
  assert.equal(createBacktestForm().from.length, 10)
  assert.equal(createBacktestForm().to.length, 10)
  assert.deepEqual(createFactorLab().customExperiments, [createCustomFactorExperiment(1, 'custom-1')])
  assert.equal(createBacktestForm().slippageModel, '')
  assert.equal(createBacktestForm('Adaptive').slippageModel, 'Adaptive')
})

test('backtest metadata projects server-owned execution choices and default', () => {
  const projected = projectBacktestMetadata({
    timeFrames: [{ value: 'Daily', displayName: '일봉' }],
    dataProviders: [{ value: 'Alpaca', displayName: 'Alpaca' }],
    slippageModels: [
      { value: 'Adaptive', displayName: '시장 상황 반영 (권장)', description: '변동성과 유동성 반영', isDefault: true },
      { value: 'Fixed', displayName: '고정 비율', description: '입력 비율 고정', isDefault: false }
    ]
  })

  assert.deepEqual(projected.timeFrameOptions, [['Daily', '일봉']])
  assert.deepEqual(projected.dataSourceOptions, [['', '기본 설정'], ['Alpaca', 'Alpaca']])
  assert.deepEqual(projected.slippageOptions[0], ['Adaptive', '시장 상황 반영 (권장)', '변동성과 유동성 반영'])
  assert.equal(projected.defaultSlippageModel, 'Adaptive')
  assert.throws(() => projectBacktestMetadata({}), /메타데이터가 비어 있습니다/)
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

test('tuning context preserves the exact successful backtest baseline', () => {
  const context = buildOptimizationContext(
    { symbolsText: 'TQQQ', from: '2025-01-01', to: '2026-01-01', timeFrame: 'Daily', dataSource: 'Alpaca' },
    { totalReturn: .2, maxDrawdown: .1, totalTrades: 12, sortinoRatio: 1.4 },
    { id: 3, name: '추세' }
  )
  assert.equal(context.patternId, 3)
  assert.equal(context.symbolsText, 'TQQQ')
  assert.deepEqual(context.baseline, { totalReturn: .2, maxDrawdown: .1, tradeCount: 12, sortinoRatio: 1.4 })
})

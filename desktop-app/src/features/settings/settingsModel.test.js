import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildSettingsRequest,
  createSettingsForm,
  parseWatchlist,
  setPatternEnabled,
  validateSettingsForm
} from './settingsModel.js'

function response() {
  return {
    orderMode: 'AlertOnly',
    preferredDataSource: 'Alpaca',
    enabledPatterns: ['Breakout'],
    watchlistSymbols: ['SPY', 'TQQQ'],
    soundAlerts: true,
    accountSize: 100000,
    riskPerTradePercent: 0.01,
    dailyLossLimitPercent: 0.03,
    maxTotalPositions: 7,
    maxPositionsPerSector: 2,
    minExpectancy: 0,
    orderModes: [
      { code: 'AlertOnly', displayName: '알림만 받기' },
      { code: 'AutoOrder', displayName: '자동 주문' }
    ],
    dataProviders: [{ code: 'Alpaca', displayName: 'Alpaca' }],
    patterns: [
      { code: 'Breakout', displayName: '가격 돌파' },
      { code: 'Tqqq200Sma', displayName: 'TQQQ 200일 이동평균선' }
    ]
  }
}

test('settings form consumes the camel-case explicit API contract and server catalogs', () => {
  const form = createSettingsForm(response())

  assert.equal(form.orderMode, 'AlertOnly')
  assert.equal(form.preferredDataSource, 'Alpaca')
  assert.equal(form.watchlistText, 'SPY, TQQQ')
  assert.deepEqual(form.patterns.map((item) => item.label), ['가격 돌파', 'TQQQ 200일 이동평균선'])
})

test('settings metadata fails closed instead of inventing unsupported defaults', () => {
  assert.throws(() => createSettingsForm({}), /선택 정보가 비어/)
  assert.throws(
    () => createSettingsForm({ ...response(), preferredDataSource: 'Missing' }),
    /지원 범위/)
})

test('watchlist and update payload normalize symbols and numeric values', () => {
  const form = createSettingsForm(response())
  form.watchlistText = ' tqqq, SPY\n069500 tqqq '
  form.accountSize = '250000'

  const payload = buildSettingsRequest(form)

  assert.deepEqual(parseWatchlist(form.watchlistText), ['TQQQ', 'SPY', '069500'])
  assert.deepEqual(payload.watchlistSymbols, ['TQQQ', 'SPY', '069500'])
  assert.equal(payload.accountSize, 250000)
  assert.equal('id' in payload, false)
})

test('built-in pattern selection supports explicit enable and disable', () => {
  const form = createSettingsForm(response())
  const enabled = setPatternEnabled(form, 'Tqqq200Sma', true)
  const disabled = setPatternEnabled(enabled, 'Breakout', false)

  assert.deepEqual(enabled.enabledPatterns, ['Breakout', 'Tqqq200Sma'])
  assert.deepEqual(disabled.enabledPatterns, ['Tqqq200Sma'])
  assert.deepEqual(form.enabledPatterns, ['Breakout'])
})

test('client validation catches risk and position-limit mistakes before saving', () => {
  const form = createSettingsForm(response())
  form.riskPerTradePercent = 2
  form.maxTotalPositions = 2
  form.maxPositionsPerSector = 3

  assert.equal(validateSettingsForm(form).length, 2)
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { createPatternWorkspaceModel } from './patternWorkspace.js'

function createModel() {
  return createPatternWorkspaceModel({
    indicatorFieldConfigs: {
      RSI: [{ key: 'period', defaultValue: 14 }],
      MACD_HIST: [{ key: 'fast', defaultValue: 12 }, { key: 'slow', defaultValue: 26 }, { key: 'signal', defaultValue: 9 }],
      BREAKOUT_HIGH: [{ key: 'period', defaultValue: 20 }]
    },
    dynamicExitFieldConfigs: {
      stop: { ATR: [{ key: 'multiplier', defaultValue: 2 }], SMA: [{ key: 'period', defaultValue: 20 }] },
      target: { ATR: [{ key: 'multiplier', defaultValue: 3 }] }
    }
  })
}

test('legacy flat rules are promoted into grouped buy and sell situations', () => {
  const source = {
    id: 7,
    name: '레거시 전략',
    entryLogic: 'OR',
    exitRulesLogic: 'AND',
    entryGroupsJson: '[]',
    entryRulesJson: JSON.stringify([{ Indicator: 'RSI', Params: { period: '9' }, Operator: '<', Value: '30' }]),
    exitGroupsJson: '[]',
    exitRulesJson: JSON.stringify([{ indicator: 'RSI', params: { period: 14 }, operator: '>=', value: 70 }])
  }
  const workspace = createModel().buildWorkspace(source)

  assert.equal(workspace.entryGroups[0].label, '매수 상황 1')
  assert.equal(workspace.entryGroups[0].logic, 'OR')
  assert.equal(workspace.entryGroups[0].rules[0].params.period, 9)
  assert.equal(workspace.exitGroups[0].label, '매도 상황 1')
  assert.equal(workspace.exitGroups[0].logic, 'AND')
  assert.equal(workspace.raw, source)
  assert.equal(source.entryGroupsJson, '[]')
})

test('malformed optional JSON falls back to safe workspace defaults', () => {
  const workspace = createModel().buildWorkspace({ name: '손상 복구', entryGroupsJson: '{broken', dynamicExitJson: '{broken' })

  assert.equal(workspace.entryGroups.length, 1)
  assert.equal(workspace.entryGroups[0].rules[0].indicator, 'RSI')
  assert.equal(workspace.dynamicExit.stopType, 'ATR')
  assert.deepEqual(workspace.dynamicExit.stopParams, { multiplier: 2 })
  assert.deepEqual(workspace.timeFilter, { allowedDaysOfWeek: [], blockedMonths: [] })
})

test('catalog defaults normalize indicator aliases and legacy dynamic exit values', () => {
  const model = createModel()
  const breakout = model.buildRuleParams('BREAKOUT_HIGH', { lookback: '55' })
  const macd = model.buildRuleParams('MACD_HIST', { stdDev: '2' })
  const stop = model.normalizeDynamicParams('stop', 'ATR', { value: '2.5' })

  assert.deepEqual(breakout, { period: 55 })
  assert.deepEqual(macd, { stddev: 2, fast: 12, slow: 26, signal: 9 })
  assert.deepEqual(stop, { multiplier: 2.5 })
})

test('payload serialization clears legacy flat rules and normalizes nested numeric values', () => {
  const model = createModel()
  const workspace = model.buildWorkspace({
    id: 11,
    name: '직렬화 전략',
    entryGroupsJson: JSON.stringify([{ label: '진입', logic: 'AND', rules: [{ indicator: 'RSI', params: { period: 14 }, operator: '<', value: 30 }] }]),
    exitGroupsJson: '[]',
    weightTiersJson: '[]',
    scalingRulesJson: '[]'
  })
  workspace.atrStopMultiplier = '2.25'
  workspace.entryGroups[0].rules[0].withinBars = '-3'
  workspace.entryGroups[0].rules[0].consecutiveBars = '2'
  workspace.entryGroups[0].rules[0].weight = '1.5'
  workspace.entryGroups[0].rules[0].refSymbol = ' SPY '
  workspace.dynamicExit.stopParams.multiplier = '2.75'

  const payload = model.buildPatternPayload(workspace)
  const entryRule = JSON.parse(payload.entryGroupsJson)[0].rules[0]

  assert.equal(payload.id, 11)
  assert.equal(payload.atrStopMultiplier, 2.25)
  assert.equal(payload.entryRulesJson, '[]')
  assert.equal(payload.exitRulesJson, '[]')
  assert.equal(entryRule.withinBars, 0)
  assert.equal(entryRule.consecutiveBars, 2)
  assert.equal(entryRule.weight, 1.5)
  assert.equal(entryRule.refSymbol, 'SPY')
  assert.deepEqual(JSON.parse(payload.dynamicExitJson).stopParams, { multiplier: 2.75 })
})

test('workspace payload round trip preserves grouped execution semantics', () => {
  const model = createModel()
  const first = model.buildWorkspace({
    name: '왕복 전략',
    entryGroupsLogic: 'OR',
    exitGroupsLogic: 'AND',
    entryGroupsJson: JSON.stringify([{ label: '반등', logic: 'AND', rules: [{ indicator: 'RSI', params: { period: 7 }, operator: '<=', value: 25, weight: 2 }] }]),
    exitGroupsJson: JSON.stringify([{ label: '과열', logic: 'OR', rules: [{ indicator: 'RSI', params: { period: 7 }, operator: '>=', value: 75 }] }]),
    scalingRulesJson: '[]',
    weightTiersJson: '[]'
  })
  const second = model.buildWorkspace(model.buildPatternPayload(first))

  assert.equal(second.entryGroupsLogic, 'OR')
  assert.equal(second.exitGroupsLogic, 'AND')
  assert.equal(second.entryGroups[0].rules[0].weight, 2)
  assert.equal(second.exitGroups[0].rules[0].operator, '>=')
})

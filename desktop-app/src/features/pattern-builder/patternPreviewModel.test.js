import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildPatternPreviewModel,
  findSelectedRule,
  summarizeRule
} from './patternPreviewModel.js'

const labels = {
  indicators: { RSI: 'RSI 과매도', SMA: '이동평균' },
  parameters: { period: '기간' },
  operators: { '<=': '이하' },
  entryModes: { NextOpen: '다음 봉 시가' },
  logicModes: { AND: '모두 충족' },
  stopTypes: { ATR: 'ATR 손절' },
  targetTypes: { R_MULTIPLE: '손실 대비 목표' }
}

function workspace() {
  return {
    name: '미리보기 모델',
    entryMode: 'NextOpen',
    atrStopMultiplier: 2,
    atrTargetMultiplier: 3,
    dynamicExit: { stopType: 'ATR', targetType: 'R_MULTIPLE' },
    entryGroups: [{
      label: '눌림 매수', logic: 'AND',
      rules: [{
        indicator: 'RSI', params: { period: 14 }, operator: '<=', value: 30,
        withinBars: 3, consecutiveBars: 2
      }]
    }],
    exitGroups: [{ label: '과열 매도', logic: 'AND', rules: [] }],
    weightTiers: [],
    scalingRules: []
  }
}

test('selected rule lookup and summary preserve the chart explanation contract', () => {
  const source = workspace()
  const node = { type: 'entryRule', groupIndex: 0, ruleIndex: 0 }
  const rule = findSelectedRule(source, node)

  assert.equal(rule, source.entryGroups[0].rules[0])
  assert.equal(
    summarizeRule(rule, labels),
    'RSI 과매도(기간:14) 이하 30 · 최근 3봉 내 · 2봉 연속'
  )
})

test('preview model combines the serialized strategy with selection-aware explanations', () => {
  const source = workspace()
  const buildPayload = (value) => ({ name: value.name, serialized: true })

  const general = buildPatternPreviewModel(source, { type: 'general' }, buildPayload, labels)
  const exit = buildPatternPreviewModel(source, { type: 'dynamicExit' }, buildPayload, labels)
  const group = buildPatternPreviewModel(source, { type: 'group', groupIndex: 0 }, buildPayload, labels)

  assert.deepEqual(general.pattern, { name: '미리보기 모델', serialized: true })
  assert.equal(general.selectedRuleSummary, '다음 봉 시가 · 손절 2 ATR · 목표 3 ATR')
  assert.equal(exit.selectedRuleSummary, '손절 ATR 손절 · 목표 손실 대비 목표')
  assert.equal(group.selectedRuleSummary, '눌림 매수 · 모두 충족')
  assert.deepEqual(buildPatternPreviewModel(null, null, buildPayload, labels), {
    pattern: null,
    selectedRuleSummary: ''
  })
})

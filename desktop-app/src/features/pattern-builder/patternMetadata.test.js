import test from 'node:test'
import assert from 'node:assert/strict'
import { projectPatternMetadata } from './patternMetadata.js'

function metadata() {
  return {
    indicators: [
      {
        code: 'RSI', displayName: 'RSI', category: '모멘텀', defaultOperator: '<=',
        defaultThreshold: 30, valueGuide: '0~100',
        parameters: [{ key: 'period', displayName: '기간', defaultValue: 14, step: 1, mustBePositive: true }]
      },
      {
        code: 'GAP', displayName: '갭', category: '가격 구조', defaultOperator: '>=',
        defaultThreshold: 2, parameters: []
      }
    ],
    ruleOperators: ['>=', '<='],
    timeFrames: [{ value: 'Daily', displayName: '일봉' }],
    entryModes: [{ code: 'NextOpen', displayName: '다음 봉 시가' }],
    sizingModes: [{ code: 'RiskBased', displayName: '손실 한도 기준' }],
    logicModes: [{ code: 'AND', displayName: '모두 충족' }],
    scalingDirections: [{ code: 'SCALE_OUT', displayName: '나눠 팔기' }],
    stopMethods: [{
      code: 'ATR', displayName: 'ATR 손절',
      parameters: [{ key: 'multiplier', displayName: '배수', defaultValue: 2, step: 0.1 }]
    }],
    targetMethods: [{ code: 'R_MULTIPLE', displayName: '손실 대비 목표', parameters: [] }],
    liveStrategyConstraints: { supportsScaling: false }
  }
}

test('server metadata projects ordered palettes, defaults, labels, and execution constraints', () => {
  const source = metadata()
  const result = projectPatternMetadata(source)

  assert.deepEqual(result.indicatorPalette.map((group) => group.title), ['모멘텀', '가격 구조'])
  assert.deepEqual(result.indicatorOptions[0], {
    label: 'RSI', indicator: 'RSI', operator: '<=', value: 30, params: { period: 14 }
  })
  assert.deepEqual(result.timeFrameOptions, [{ value: 'Daily', label: '일봉' }])
  assert.equal(result.entryModeLabels.NextOpen, '다음 봉 시가')
  assert.deepEqual(result.dynamicExitFieldConfigs.stop.ATR, [
    { key: 'multiplier', label: '배수', step: '0.1', defaultValue: 2 }
  ])
  assert.equal(result.indicatorSet.has('GAP'), true)
  assert.equal(result.positiveParamKeys.has('period'), true)
  assert.equal(result.liveStrategyConstraints.supportsScaling, false)
  assert.equal(source.indicators[0].parameters[0].step, 1)
})

test('incomplete server metadata fails closed before the editor becomes interactive', () => {
  assert.throws(
    () => projectPatternMetadata({ indicators: [] }),
    /전략 구성 메타데이터가 비어 있습니다/
  )
})

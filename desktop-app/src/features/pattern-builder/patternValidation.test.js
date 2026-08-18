import test from 'node:test'
import assert from 'node:assert/strict'
import { collectPatternValidationIssues } from './patternValidation.js'

function validWorkspace(overrides = {}) {
  return {
    name: '검증 전략',
    atrStopMultiplier: 2,
    atrTargetMultiplier: 3,
    maxHoldingBars: 10,
    trailingAtr: 0,
    partialProfitR: 0,
    defaultAllocationPercent: 100,
    entryGroups: [{ rules: [{ indicator: 'RSI', params: { period: 14 }, compareParams: {}, withinBars: 0, consecutiveBars: 0, weight: 1 }] }],
    exitGroups: [],
    useWeightTiers: false,
    weightTiers: [],
    scalingRules: [],
    circuitBreaker: { consecutiveLossLimit: 0, cooldownBars: 5, maxDrawdownPercent: 0 },
    enableLiveTrading: false,
    timeFrame: 'Daily',
    entryMode: 'CurrentClose',
    reentry: { cooldownBarsAfterLoss: 0, cooldownBarsAfterWin: 0 },
    portfolioRules: { maxTotalPositions: 0, maxSinglePositionPercent: 0, maxEntriesPerDay: 0, maxCorrelation: 0 },
    ...overrides
  }
}

const context = {
  indicatorSet: new Set(['RSI', 'MACD_HIST']),
  positiveParamKeys: new Set(['period', 'fast', 'slow']),
  paramKeyLabels: { period: '기간', fast: '빠른 기간', slow: '느린 기간' }
}

test('valid pattern workspace has no validation issues', () => {
  assert.deepEqual(collectPatternValidationIssues(validWorkspace(), context), [])
})

test('rule validation rejects conflicting lookback modes and invalid MACD ordering', () => {
  const workspace = validWorkspace({
    entryGroups: [{ rules: [{ indicator: 'MACD_HIST', params: { fast: 26, slow: 12 }, compareParams: {}, withinBars: 3, consecutiveBars: 2, weight: 1 }] }]
  })
  const issues = collectPatternValidationIssues(workspace, context)

  assert.ok(issues.some((issue) => issue.includes('동시에 사용할 수 없습니다')))
  assert.ok(issues.some((issue) => issue.includes('빠른 EMA는 느린 EMA보다 작아야 합니다')))
})

test('sell groups, weight tiers, and scaling rules cannot silently contain empty conditions', () => {
  const workspace = validWorkspace({
    exitGroups: [{ rules: [] }],
    useWeightTiers: true,
    weightTiers: [{ allocationPercent: 101, conditions: [] }],
    scalingRules: [{ percent: 0, maxCount: 0, conditions: [] }]
  })
  const issues = collectPatternValidationIssues(workspace, context)

  assert.ok(issues.some((issue) => issue.startsWith('매도 상황 1')))
  assert.ok(issues.some((issue) => issue.startsWith('매수 비중 1')))
  assert.ok(issues.some((issue) => issue.startsWith('추가 매수·분할 매도 1')))
})

test('live trading rejects strategy features not supported by the execution engine', () => {
  const workspace = validWorkspace({
    enableLiveTrading: true,
    timeFrame: 'Weekly',
    entryMode: 'NextOpen',
    partialProfitR: 1,
    scalingRules: [{ percent: 50, maxCount: 1, conditions: [{ indicator: 'RSI', params: { period: 14 }, compareParams: {}, withinBars: 0, consecutiveBars: 0, weight: 1 }] }]
  })
  const issues = collectPatternValidationIssues(workspace, {
    ...context,
    liveStrategyConstraints: {
      supportedTimeFrames: ['Daily'],
      supportedEntryModes: ['CurrentClose'],
      supportsPartialExit: false,
      supportsScaling: false
    }
  })

  assert.ok(issues.some((issue) => issue.includes('시간축')))
  assert.ok(issues.some((issue) => issue.includes('체결 시점')))
  assert.ok(issues.some((issue) => issue.includes('부분 익절')))
  assert.ok(issues.some((issue) => issue.includes('추가 매수·분할 매도 전략')))
})

test('live trading accepts partial profit when the server execution engine supports it', () => {
  const workspace = validWorkspace({
    enableLiveTrading: true,
    timeFrame: 'Daily',
    entryMode: 'NextOpen',
    partialProfitR: 1,
    scalingRules: []
  })
  const issues = collectPatternValidationIssues(workspace, {
    ...context,
    liveStrategyConstraints: {
      supportedTimeFrames: ['Daily'],
      supportedEntryModes: ['NextOpen'],
      supportsPartialExit: true,
      supportsScaling: false
    }
  })

  assert.ok(!issues.some((issue) => issue.includes('부분 익절')))
})

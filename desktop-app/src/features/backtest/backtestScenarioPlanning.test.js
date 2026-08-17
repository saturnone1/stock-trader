import test from 'node:test'
import assert from 'node:assert/strict'
import {
  buildFactorExperimentDefinitions,
  buildScenarioPatterns,
  buildTimingScenarios,
  buildUniverseVariants,
  combineScenarioPlans,
  factorScenarioScore
} from './backtestScenarioPlanning.js'

test('timing overlay preserves the source and appends conservative entry and OR exit rules', () => {
  const source = [{ name: '반등', raw: { entryRulesJson: '[{"indicator":"RSI"}]', entryLogic: 'AND', exitRulesJson: '[]', requireBullRegime: true } }]
  const result = buildScenarioPatterns(source, { type: 'overlay', structure: 'market-stock', entryPeriod: 20, exitPeriod: 10, label: '시장+종목 20/10' }, 'spy')
  const entryGroups = JSON.parse(result[0].entryGroupsJson)
  const exitRules = JSON.parse(result[0].exitRulesJson)

  assert.equal(entryGroups[0].rules.length, 3)
  assert.equal(entryGroups[0].rules[1].refSymbol, 'SPY')
  assert.equal(entryGroups[0].rules[2].refSymbol, '')
  assert.equal(exitRules.length, 2)
  assert.equal(exitRules[0].params.period, 10)
  assert.equal(exitRules[1].params.period, 20)
  assert.equal(result[0].exitRulesLogic, 'OR')
  assert.equal(result[0].requireBullRegime, false)
  assert.equal(source[0].raw.exitRulesJson, '[]')
})

test('timing scenarios are the cartesian selection plus an optional baseline', () => {
  const scenarios = buildTimingScenarios(
    { includeBaseScenario: true, selectedStructures: ['market', 'market-stock'], selectedWindows: ['20-10'] },
    [{ id: 'market', label: '시장 타이밍' }, { id: 'market-stock', label: '시장 + 종목 타이밍' }],
    [{ id: '20-10', label: '20 / 10', entryPeriod: 20, exitPeriod: 10 }])

  assert.deepEqual(scenarios.map((item) => item.key), ['base', 'market-20-10', 'market-stock-20-10'])
})

test('factor definitions normalize inputs and remove duplicate experiments', () => {
  const definitions = buildFactorExperimentDefinitions(
    { selectedPresets: ['value'], includeCurrentBuilder: true, customExperiments: [{ id: 'custom', label: '같은 조건', peRatioMax: ' 15 ', positiveEarningsOnly: true }] },
    [{ id: 'value', label: '저PER', note: '', params: { peRatioMax: '15', positiveEarningsOnly: true, sortBy: 'peAsc' } }],
    {})

  assert.equal(definitions.length, 1)
  assert.deepEqual(definitions[0].params, { peRatioMax: '15', positiveEarningsOnly: true })
})

test('universe variants deduplicate identical symbol sets and scenario plans retain group metadata', () => {
  const variants = buildUniverseVariants({
    baseSymbols: ['aapl', 'MSFT'],
    extraVariants: [{ key: 'duplicate', label: '중복', symbols: ['MSFT', 'AAPL'] }],
    universeComparison: { enabled: true, includeCurrentSymbols: true, includeUniverseBuilder: true, includeFinancialFactor: true, includeCombined: true },
    universeBuilderSymbols: ['AAPL'],
    financialFactorSymbols: ['AAPL']
  })
  const plans = combineScenarioPlans(variants, [{ key: 'base', label: '기본', description: '기본 설명' }])

  assert.deepEqual(variants.map((item) => item.key), ['current', 'universe'])
  assert.equal(plans[1].key, 'universe::base')
  assert.equal(plans[1].comparisonGroupKey, 'universe')
  assert.deepEqual(plans[1].symbols, ['AAPL'])
})

test('factor ranking score preserves each ranking policy formula', () => {
  const entry = { data: { totalReturn: 0.2, sharpeRatio: 1.5, maxDrawdown: 0.1, totalTrades: 100 } }
  assert.equal(factorScenarioScore(entry, 'best-return'), (0.2 * 140) + (1.5 * 12) - (0.1 * 35) + (80 * 0.04))
  assert.equal(factorScenarioScore(entry, 'defensive'), (0.2 * 60) + (1.5 * 40) - (0.1 * 95) + (80 * 0.03))
})

import test from 'node:test'
import assert from 'node:assert/strict'
import { createPatternEditorCommands } from './patternEditorCommands.js'

const commands = createPatternEditorCommands({
  blankRule: (template = {}) => ({ indicator: template.indicator ?? 'RSI', params: { period: 14 } }),
  blankGroup: (label) => ({ label, logic: 'AND', rules: [{ indicator: 'DEFAULT' }] }),
  blankExitGroup: (label) => ({ label, logic: 'AND', rules: [{ indicator: 'DEFAULT_EXIT' }] }),
  blankWeightTier: () => ({ label: '기본 비중', conditions: [{ indicator: 'RSI' }] }),
  blankScalingRule: () => ({ direction: 'SCALE_IN', conditions: [{ indicator: 'RSI' }] })
})

function workspace(overrides = {}) {
  return {
    entryGroups: [],
    exitGroups: [],
    weightTiers: [],
    scalingRules: [],
    useWeightTiers: false,
    ...overrides
  }
}

test('adding a rule to an empty tree creates exactly the requested buy or sell condition', () => {
  const source = workspace()
  const buy = commands.addEntryRule(source, { type: 'general' }, { indicator: 'MACD_HIST' })
  const sell = commands.addExitRule(source, { type: 'general' }, { indicator: 'RSI' })

  assert.equal(buy.workspace.entryGroups.length, 1)
  assert.deepEqual(buy.workspace.entryGroups[0].rules.map((rule) => rule.indicator), ['MACD_HIST'])
  assert.deepEqual(buy.selectedNode, { type: 'entryRule', groupIndex: 0, ruleIndex: 0 })
  assert.deepEqual(sell.workspace.exitGroups[0].rules.map((rule) => rule.indicator), ['RSI'])
  assert.deepEqual(source, workspace())
})

test('node creation enables weight tiers and returns the exact new selection', () => {
  const source = workspace()
  const tier = commands.addNode(source, { type: 'general' }, 'weightTier')
  const scaling = commands.addNode(source, { type: 'general' }, 'scalingRule')

  assert.equal(tier.workspace.useWeightTiers, true)
  assert.equal(tier.workspace.weightTiers.length, 1)
  assert.deepEqual(tier.selectedNode, { type: 'weightTier', tierIndex: 0 })
  assert.deepEqual(scaling.selectedNode, { type: 'scalingRule', scalingIndex: 0 })
  assert.equal(source.useWeightTiers, false)
})

test('move commands cover grouped rules and do not dirty state at list boundaries', () => {
  const source = workspace({
    entryGroups: [{ label: 'A', rules: [{ indicator: 'RSI' }, { indicator: 'MACD_HIST' }] }, { label: 'B', rules: [] }]
  })
  const movedRule = commands.moveNode(source, { type: 'entryRule', groupIndex: 0, ruleIndex: 0 }, { type: 'entryRule', groupIndex: 0, ruleIndex: 0 }, 1)
  const movedGroup = commands.moveNode(source, { type: 'group', groupIndex: 0 }, { type: 'group', groupIndex: 0 }, 1)
  const boundary = commands.moveNode(source, { type: 'group', groupIndex: 0 }, { type: 'group', groupIndex: 0 }, -1)

  assert.deepEqual(movedRule.workspace.entryGroups[0].rules.map((rule) => rule.indicator), ['MACD_HIST', 'RSI'])
  assert.equal(movedRule.selectedNode.ruleIndex, 1)
  assert.deepEqual(movedGroup.workspace.entryGroups.map((group) => group.label), ['B', 'A'])
  assert.equal(boundary.changed, false)
  assert.equal(boundary.workspace, source)
})

test('duplicate commands deep-copy nodes and removal selects their parent', () => {
  const source = workspace({
    exitGroups: [{ label: '과열', rules: [{ indicator: 'RSI', params: { period: 14 } }] }]
  })
  const duplicate = commands.duplicateNode(source, { type: 'exitRule', groupIndex: 0, ruleIndex: 0 }, { type: 'exitRule', groupIndex: 0, ruleIndex: 0 })
  duplicate.workspace.exitGroups[0].rules[1].params.period = 7
  const removed = commands.removeNode(duplicate.workspace, duplicate.selectedNode, { type: 'exitRule', groupIndex: 0, ruleIndex: 1 })

  assert.equal(duplicate.workspace.exitGroups[0].rules[0].params.period, 14)
  assert.equal(source.exitGroups[0].rules.length, 1)
  assert.equal(removed.workspace.exitGroups[0].rules.length, 1)
  assert.deepEqual(removed.selectedNode, { type: 'exitGroup', groupIndex: 0 })
})

test('invalid and unsupported commands are safe no-ops', () => {
  const source = workspace()
  const invalidRemove = commands.removeNode(source, { type: 'general' }, { type: 'entryRule', groupIndex: 99, ruleIndex: 0 })
  const invalidCondition = commands.addTierCondition(source, { type: 'general' }, 4)
  const unsupported = commands.addNode(source, { type: 'general' }, 'unknown')

  assert.equal(invalidRemove.changed, false)
  assert.equal(invalidCondition.changed, false)
  assert.equal(unsupported.changed, false)
  assert.equal(invalidRemove.workspace, source)
})

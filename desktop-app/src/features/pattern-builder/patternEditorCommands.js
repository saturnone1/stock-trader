function cloneValue(value) {
  return JSON.parse(JSON.stringify(value))
}

function unchanged(workspace, selectedNode) {
  return { workspace, selectedNode, changed: false }
}

function changed(workspace, selectedNode) {
  return { workspace, selectedNode, changed: true }
}

function validIndex(list, index) {
  return Array.isArray(list) && Number.isInteger(index) && index >= 0 && index < list.length
}

export function createPatternEditorCommands({ blankRule, blankGroup, blankExitGroup, blankWeightTier, blankScalingRule }) {
  function selectedEntryGroupIndex(workspace, selectedNode) {
    if (selectedNode.type === 'group' || selectedNode.type === 'entryRule') return selectedNode.groupIndex
    return workspace?.entryGroups?.length ? 0 : -1
  }

  function selectedExitGroupIndex(workspace, selectedNode) {
    if (selectedNode.type === 'exitGroup' || selectedNode.type === 'exitRule') return selectedNode.groupIndex
    return workspace?.exitGroups?.length ? 0 : -1
  }

  function addEntryRule(workspace, selectedNode, template = {}) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    let groupIndex = selectedEntryGroupIndex(next, selectedNode)
    if (!validIndex(next.entryGroups, groupIndex)) {
      const group = blankGroup('매수 상황 1')
      group.rules = []
      next.entryGroups.push(group)
      groupIndex = next.entryGroups.length - 1
    }
    next.entryGroups[groupIndex].rules.push(blankRule(template))
    return changed(next, { type: 'entryRule', groupIndex, ruleIndex: next.entryGroups[groupIndex].rules.length - 1 })
  }

  function addExitRule(workspace, selectedNode, template = {}) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    let groupIndex = selectedExitGroupIndex(next, selectedNode)
    if (!validIndex(next.exitGroups, groupIndex)) {
      const group = blankExitGroup('매도 상황 1')
      group.rules = []
      next.exitGroups.push(group)
      groupIndex = next.exitGroups.length - 1
    }
    next.exitGroups[groupIndex].rules.push(blankRule(template))
    return changed(next, { type: 'exitRule', groupIndex, ruleIndex: next.exitGroups[groupIndex].rules.length - 1 })
  }

  function addNode(workspace, selectedNode, kind) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    if (kind === 'group') {
      next.entryGroups.push(blankGroup(`매수 상황 ${next.entryGroups.length + 1}`))
      return changed(next, { type: 'group', groupIndex: next.entryGroups.length - 1 })
    }
    if (kind === 'exitGroup') {
      next.exitGroups.push(blankExitGroup(`매도 상황 ${next.exitGroups.length + 1}`))
      return changed(next, { type: 'exitGroup', groupIndex: next.exitGroups.length - 1 })
    }
    if (kind === 'weightTier') {
      next.weightTiers.push(blankWeightTier())
      next.useWeightTiers = true
      return changed(next, { type: 'weightTier', tierIndex: next.weightTiers.length - 1 })
    }
    if (kind === 'scalingRule') {
      next.scalingRules.push(blankScalingRule())
      return changed(next, { type: 'scalingRule', scalingIndex: next.scalingRules.length - 1 })
    }
    return unchanged(workspace, selectedNode)
  }

  function addTierCondition(workspace, selectedNode, tierIndex) {
    if (!workspace || !validIndex(workspace.weightTiers, tierIndex)) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    next.weightTiers[tierIndex].conditions.push(blankRule())
    return changed(next, { type: 'tierRule', tierIndex, ruleIndex: next.weightTiers[tierIndex].conditions.length - 1 })
  }

  function addScalingCondition(workspace, selectedNode, scalingIndex) {
    if (!workspace || !validIndex(workspace.scalingRules, scalingIndex)) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    next.scalingRules[scalingIndex].conditions.push(blankRule())
    return changed(next, { type: 'scalingRuleCondition', scalingIndex, ruleIndex: next.scalingRules[scalingIndex].conditions.length - 1 })
  }

  function removeNode(workspace, selectedNode, node) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    if (node.type === 'group' && validIndex(next.entryGroups, node.groupIndex)) {
      next.entryGroups.splice(node.groupIndex, 1)
      return changed(next, { type: 'general' })
    }
    if (node.type === 'entryRule' && validIndex(next.entryGroups, node.groupIndex) && validIndex(next.entryGroups[node.groupIndex].rules, node.ruleIndex)) {
      next.entryGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      return changed(next, { type: 'group', groupIndex: node.groupIndex })
    }
    if (node.type === 'exitGroup' && validIndex(next.exitGroups, node.groupIndex)) {
      next.exitGroups.splice(node.groupIndex, 1)
      return changed(next, { type: 'general' })
    }
    if (node.type === 'exitRule' && validIndex(next.exitGroups, node.groupIndex) && validIndex(next.exitGroups[node.groupIndex].rules, node.ruleIndex)) {
      next.exitGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      return changed(next, { type: 'exitGroup', groupIndex: node.groupIndex })
    }
    if (node.type === 'weightTier' && validIndex(next.weightTiers, node.tierIndex)) {
      next.weightTiers.splice(node.tierIndex, 1)
      return changed(next, { type: 'general' })
    }
    if (node.type === 'tierRule' && validIndex(next.weightTiers, node.tierIndex) && validIndex(next.weightTiers[node.tierIndex].conditions, node.ruleIndex)) {
      next.weightTiers[node.tierIndex].conditions.splice(node.ruleIndex, 1)
      return changed(next, { type: 'weightTier', tierIndex: node.tierIndex })
    }
    if (node.type === 'scalingRule' && validIndex(next.scalingRules, node.scalingIndex)) {
      next.scalingRules.splice(node.scalingIndex, 1)
      return changed(next, { type: 'general' })
    }
    if (node.type === 'scalingRuleCondition' && validIndex(next.scalingRules, node.scalingIndex) && validIndex(next.scalingRules[node.scalingIndex].conditions, node.ruleIndex)) {
      next.scalingRules[node.scalingIndex].conditions.splice(node.ruleIndex, 1)
      return changed(next, { type: 'scalingRule', scalingIndex: node.scalingIndex })
    }
    return unchanged(workspace, selectedNode)
  }

  function resolveNodeList(workspace, node) {
    if (node.type === 'group') return { list: workspace.entryGroups, indexKey: 'groupIndex' }
    if (node.type === 'entryRule' && validIndex(workspace.entryGroups, node.groupIndex)) return { list: workspace.entryGroups[node.groupIndex].rules, indexKey: 'ruleIndex' }
    if (node.type === 'exitGroup') return { list: workspace.exitGroups, indexKey: 'groupIndex' }
    if (node.type === 'exitRule' && validIndex(workspace.exitGroups, node.groupIndex)) return { list: workspace.exitGroups[node.groupIndex].rules, indexKey: 'ruleIndex' }
    if (node.type === 'weightTier') return { list: workspace.weightTiers, indexKey: 'tierIndex' }
    if (node.type === 'tierRule' && validIndex(workspace.weightTiers, node.tierIndex)) return { list: workspace.weightTiers[node.tierIndex].conditions, indexKey: 'ruleIndex' }
    if (node.type === 'scalingRule') return { list: workspace.scalingRules, indexKey: 'scalingIndex' }
    if (node.type === 'scalingRuleCondition' && validIndex(workspace.scalingRules, node.scalingIndex)) return { list: workspace.scalingRules[node.scalingIndex].conditions, indexKey: 'ruleIndex' }
    return null
  }

  function moveNode(workspace, selectedNode, node, offset) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    const resolved = resolveNodeList(next, node)
    if (!resolved || !Number.isInteger(offset)) return unchanged(workspace, selectedNode)
    const index = node[resolved.indexKey]
    const target = index + offset
    if (!validIndex(resolved.list, index) || !validIndex(resolved.list, target)) return unchanged(workspace, selectedNode)
    const [item] = resolved.list.splice(index, 1)
    resolved.list.splice(target, 0, item)
    return changed(next, { ...node, [resolved.indexKey]: target })
  }

  function duplicateNode(workspace, selectedNode, node) {
    if (!workspace) return unchanged(workspace, selectedNode)
    const next = cloneValue(workspace)
    const resolved = resolveNodeList(next, node)
    if (!resolved) return unchanged(workspace, selectedNode)
    const index = node[resolved.indexKey]
    if (!validIndex(resolved.list, index)) return unchanged(workspace, selectedNode)
    resolved.list.splice(index + 1, 0, cloneValue(resolved.list[index]))
    return changed(next, selectedNode)
  }

  return { addEntryRule, addExitRule, addNode, addTierCondition, addScalingCondition, removeNode, moveNode, duplicateNode }
}

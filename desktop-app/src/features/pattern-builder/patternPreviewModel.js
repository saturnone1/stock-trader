function display(labels, value) {
  return labels?.[value] ?? value
}

export function findSelectedRule(workspace, selectedNode) {
  if (!workspace || !selectedNode) return null
  if (selectedNode.type === 'entryRule') {
    return workspace.entryGroups?.[selectedNode.groupIndex]?.rules?.[selectedNode.ruleIndex] ?? null
  }
  if (selectedNode.type === 'exitRule') {
    return workspace.exitGroups?.[selectedNode.groupIndex]?.rules?.[selectedNode.ruleIndex] ?? null
  }
  if (selectedNode.type === 'tierRule') {
    return workspace.weightTiers?.[selectedNode.tierIndex]?.conditions?.[selectedNode.ruleIndex] ?? null
  }
  if (selectedNode.type === 'scalingRuleCondition') {
    return workspace.scalingRules?.[selectedNode.scalingIndex]?.conditions?.[selectedNode.ruleIndex] ?? null
  }
  return null
}

export function summarizeRule(rule, labels) {
  const indicator = display(labels.indicators, rule.indicator)
  const params = Object.entries(rule.params || {})
    .map(([key, value]) => `${labels.parameters?.[key] ?? key}:${value}`)
    .join(', ')
  const comparison = rule.compareIndicator
    ? ` 대비 ${display(labels.indicators, rule.compareIndicator)}`
    : ` ${labels.operators?.[rule.operator] ?? rule.operator} ${rule.value}`
  const timing = [
    rule.withinBars ? `최근 ${rule.withinBars}봉 내` : '',
    rule.consecutiveBars ? `${rule.consecutiveBars}봉 연속` : ''
  ].filter(Boolean).join(' · ')
  return `${indicator}${params ? `(${params})` : ''}${comparison}${timing ? ` · ${timing}` : ''}`
}

export function buildPatternPreviewModel(workspace, selectedNode, buildPayload, labels) {
  if (!workspace) return { pattern: null, selectedRuleSummary: '' }

  const rule = findSelectedRule(workspace, selectedNode)
  if (rule) {
    return {
      pattern: buildPayload(workspace),
      selectedRuleSummary: summarizeRule(rule, labels)
    }
  }

  let selectedRuleSummary = ''
  if (selectedNode?.type === 'dynamicExit') {
    selectedRuleSummary = `손절 ${display(labels.stopTypes, workspace.dynamicExit.stopType)} · 목표 ${display(labels.targetTypes, workspace.dynamicExit.targetType)}`
  } else if (selectedNode?.type === 'general') {
    selectedRuleSummary = `${display(labels.entryModes, workspace.entryMode)} · 손절 ${workspace.atrStopMultiplier} ATR · 목표 ${workspace.atrTargetMultiplier} ATR`
  } else if (selectedNode?.type === 'group') {
    const group = workspace.entryGroups?.[selectedNode.groupIndex]
    selectedRuleSummary = `${group?.label ?? '매수 상황'} · ${display(labels.logicModes, group?.logic)}`
  } else if (selectedNode?.type === 'exitGroup') {
    const group = workspace.exitGroups?.[selectedNode.groupIndex]
    selectedRuleSummary = `${group?.label ?? '매도 상황'} · ${display(labels.logicModes, group?.logic)}`
  }

  return { pattern: buildPayload(workspace), selectedRuleSummary }
}

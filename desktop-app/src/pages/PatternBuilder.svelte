<script>
  import { onMount } from 'svelte'
  import { ArrowDown, ArrowUp, ChevronRight, CircleHelp, Copy, FolderTree, Plus, Save, Trash2 } from 'lucide-svelte'
  import { metadataApi, patternApi } from '../api/endpoints'
  import PatternPreview from '../lib/PatternPreview.svelte'
  import { collectPatternValidationIssues } from '../features/pattern-builder/patternValidation'
  import { createPatternWorkspaceModel } from '../features/pattern-builder/patternWorkspace'

  const workspaceModel = createPatternWorkspaceModel()

  let indicatorPalette = []
  let operatorOptions = []
  let entryModeOptions = []
  let timeFrameOptions = []
  let sizingModeOptions = []
  let logicOptions = []
  let scalingDirectionOptions = []
  let stopTypeOptions = []
  let targetTypeOptions = []
  let indicatorOptions = []
  let indicatorSet = new Set()
  let positiveParamKeys = new Set(['multiplier', 'multiple', 'percent'])
  const dayOptions = [
    { value: 1, label: '월' }, { value: 2, label: '화' }, { value: 3, label: '수' },
    { value: 4, label: '목' }, { value: 5, label: '금' }, { value: 6, label: '토' }, { value: 0, label: '일' }
  ]
  const monthOptions = Array.from({ length: 12 }, (_, index) => index + 1)
  let indicatorLabels = {}
  let indicatorValueGuides = {}
  let indicatorFieldConfigs = {}
  const operatorLabels = {
    '>': '초과',
    '<': '미만',
    '>=': '이상',
    '<=': '이하',
    crosses_above: '상향 돌파',
    crosses_below: '하향 이탈'
  }
  let entryModeLabels = {}
  let sizingModeLabels = {}
  let logicLabels = {}
  let scalingDirectionLabels = {}
  let stopTypeLabels = {}
  let targetTypeLabels = {}
  let liveStrategyConstraints = null
  const paramKeyLabels = {
    period: '기간',
    cumulativePeriod: '누적 기간',
    bars: '봉 수',
    lookback: '되돌아보기',
    stddev: '표준편차',
    percent: '퍼센트',
    multiple: 'R 배수',
    multiplier: '배수',
    smooth: '평활',
    slow: '느린 기간',
    fast: '빠른 기간',
    signal: '시그널 기간'
  }
  const glossaryTooltips = {
    workspace: '저장한 매매 전략을 고르고 새 전략을 만드는 곳입니다.',
    pattern: '한 전략에서 언제 사고, 얼마나 사고, 언제 팔지 정하는 기본 설정입니다.',
    strategy: '매수 조건부터 손절·익절과 거래 제한까지 실제 매매 순서대로 구성합니다.',
    rule: 'RSI가 30 이하인지, 거래량이 평균보다 큰지처럼 매수·매도를 판단하는 한 가지 조건입니다.',
    entryGroup: '같이 확인할 매수 조건을 한 상황으로 묶습니다. 모든 조건 또는 하나 이상의 조건을 만족하도록 정할 수 있습니다.',
    exitRule: '보유한 종목을 언제 팔지 정하는 조건입니다.',
    weightTier: '시장 상황이나 조건에 따라 투자 비중을 다르게 정합니다.',
    scalingRule: '보유 중 추가로 사거나 일부를 팔 시점과 수량을 정합니다.',
    runtime: '거래 가능한 시기, 손실 후 휴식, 동시 보유 한도처럼 전략 전체의 안전장치를 정합니다.',
    dynamicExit: 'ATR, 이동평균, 이전 고점·저점 등을 이용해 손절가와 목표가를 계산합니다.',
    ruleInspector: '선택한 매수·매도 조건의 지표와 기준값을 바꾸는 곳입니다.',
    entryMode: '신호가 뜬 현재 봉 종가에 바로 들어갈지, 다음 봉 시가에 들어갈지 정합니다.',
    sizingMode: '주문 크기를 어떤 방식으로 계산할지 정합니다.'
  }
  let dynamicExitFieldConfigs = { stop: {}, target: {} }
  let patterns = []
  let selectedPattern = null
  let workspace = null
  let selectedNode = { type: 'general' }
  let loading = true
  let saving = false
  let dirty = false
  let error = ''
  let notice = ''
  let showNewPattern = false
  let newPatternName = ''
  let validationIssues = []
  onMount(initialize)

  async function initialize() {
    loading = true
    try {
      const metadata = await metadataApi.getStrategyBuilder()
      applyMetadata(metadata)
      await loadPatterns()
    } catch (e) {
      error = e?.response?.data?.error ?? e?.message ?? '전략 구성 정보를 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  function applyMetadata(metadata) {
    const indicators = metadata?.indicators ?? []
    const categories = []
    const grouped = new Map()

    indicatorOptions = indicators.map((item) => ({
      label: item.displayName,
      indicator: item.code,
      operator: item.defaultOperator,
      value: item.defaultThreshold,
      params: Object.fromEntries((item.parameters ?? []).map((parameter) => [parameter.key, parameter.defaultValue]))
    }))
    indicators.forEach((item) => {
      if (!grouped.has(item.category)) {
        grouped.set(item.category, [])
        categories.push(item.category)
      }
      grouped.get(item.category).push(indicatorOptions.find((option) => option.indicator === item.code))
    })
    indicatorPalette = categories.map((title) => ({ title, items: grouped.get(title) }))
    indicatorSet = new Set(indicators.map((item) => item.code))
    indicatorLabels = Object.fromEntries(indicators.map((item) => [item.code, item.displayName]))
    indicatorValueGuides = Object.fromEntries(indicators.filter((item) => item.valueGuide).map((item) => [item.code, item.valueGuide]))
    indicatorFieldConfigs = Object.fromEntries(indicators.map((item) => [item.code, (item.parameters ?? []).map((parameter) => ({
      key: parameter.key,
      label: parameter.displayName,
      step: String(parameter.step),
      defaultValue: parameter.defaultValue
    }))]))
    positiveParamKeys = new Set([
      'multiplier', 'multiple', 'percent',
      ...indicators.flatMap((item) => (item.parameters ?? []).filter((parameter) => parameter.mustBePositive).map((parameter) => parameter.key))
    ])
    operatorOptions = metadata?.ruleOperators ?? []
    timeFrameOptions = (metadata?.timeFrames ?? []).map((item) => ({ value: item.value, label: item.displayName }))
    const setOptions = (items) => (items ?? []).map((item) => item.code)
    const setLabels = (items) => Object.fromEntries((items ?? []).map((item) => [item.code, item.displayName]))
    const exitConfigs = (items) => Object.fromEntries((items ?? []).map((item) => [item.code, (item.parameters ?? []).map((parameter) => ({
      key: parameter.key, label: parameter.displayName, step: String(parameter.step), defaultValue: parameter.defaultValue
    }))]))
    entryModeOptions = setOptions(metadata?.entryModes)
    sizingModeOptions = setOptions(metadata?.sizingModes)
    logicOptions = setOptions(metadata?.logicModes)
    scalingDirectionOptions = setOptions(metadata?.scalingDirections)
    stopTypeOptions = setOptions(metadata?.stopMethods)
    targetTypeOptions = setOptions(metadata?.targetMethods)
    entryModeLabels = setLabels(metadata?.entryModes)
    sizingModeLabels = setLabels(metadata?.sizingModes)
    logicLabels = setLabels(metadata?.logicModes)
    scalingDirectionLabels = setLabels(metadata?.scalingDirections)
    stopTypeLabels = setLabels(metadata?.stopMethods)
    targetTypeLabels = setLabels(metadata?.targetMethods)
    dynamicExitFieldConfigs = { stop: exitConfigs(metadata?.stopMethods), target: exitConfigs(metadata?.targetMethods) }
    liveStrategyConstraints = metadata?.liveStrategyConstraints
    workspaceModel.configure({ indicatorFieldConfigs, dynamicExitFieldConfigs })

    if (!indicatorOptions.length || !timeFrameOptions.length || !operatorOptions.length || !entryModeOptions.length || !stopTypeOptions.length) {
      throw new Error('서버의 전략 구성 메타데이터가 비어 있습니다.')
    }
  }

  const blankRule = (...args) => workspaceModel.blankRule(...args)
  const blankGroup = (...args) => workspaceModel.blankGroup(...args)
  const blankExitGroup = (...args) => workspaceModel.blankExitGroup(...args)
  const blankWeightTier = (...args) => workspaceModel.blankWeightTier(...args)
  const blankScalingRule = (...args) => workspaceModel.blankScalingRule(...args)
  const buildWorkspace = (...args) => workspaceModel.buildWorkspace(...args)
  function touch() {
    workspace = { ...workspace }
    dirty = true
  }

  const toNumber = (...args) => workspaceModel.toNumber(...args)
  const getIndicatorFieldConfigs = (...args) => workspaceModel.getIndicatorFieldConfigs(...args)
  const buildRuleParams = (...args) => workspaceModel.buildRuleParams(...args)
  const getExtraParamEntries = (...args) => workspaceModel.getExtraParamEntries(...args)
  const sanitizeNumericMap = (...args) => workspaceModel.sanitizeNumericMap(...args)
  const getDynamicFieldConfigs = (...args) => workspaceModel.getDynamicFieldConfigs(...args)
  const normalizeDynamicParams = (...args) => workspaceModel.normalizeDynamicParams(...args)
  function setDynamicExitType(kind, type) {
    if (!workspace) return

    if (kind === 'stop') {
      workspace.dynamicExit.stopType = type
      workspace.dynamicExit.stopParams = normalizeDynamicParams('stop', type, workspace.dynamicExit.stopParams)
    } else {
      workspace.dynamicExit.targetType = type
      workspace.dynamicExit.targetParams = normalizeDynamicParams('target', type, workspace.dynamicExit.targetParams)
    }

    touch()
  }

  function updateDynamicParam(kind, key, value) {
    if (!workspace) return

    if (kind === 'stop') {
      workspace.dynamicExit.stopParams = {
        ...workspace.dynamicExit.stopParams,
        [key]: toNumber(value, workspace.dynamicExit.stopParams[key] ?? 0)
      }
    } else {
      workspace.dynamicExit.targetParams = {
        ...workspace.dynamicExit.targetParams,
        [key]: toNumber(value, workspace.dynamicExit.targetParams[key] ?? 0)
      }
    }

    touch()
  }

  const buildPatternPayload = (...args) => workspaceModel.buildPatternPayload(...args)
  $: validationIssues = workspace ? collectPatternValidationIssues(workspace, {
    indicatorSet,
    positiveParamKeys,
    paramKeyLabels,
    liveStrategyConstraints
  }) : []
  $: previewPattern = workspace ? buildPatternPayload(workspace) : null
  $: previewSelectedSummary = getSelectedPreviewSummary()

  async function loadPatterns() {
    loading = true
    try {
      const res = await patternApi.list()
      patterns = res.data || []
      error = ''
    } catch (e) {
      error = e?.message || '전략 목록을 불러오지 못했습니다.'
    } finally {
      loading = false
    }
  }

  async function createPattern() {
    if (!newPatternName.trim()) return
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 새 전략을 만들까요?')) return
    try {
      const res = await patternApi.create({ name: newPatternName, description: '' })
      newPatternName = ''
      showNewPattern = false
      await loadPatterns()
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      selectedNode = { type: 'general' }
      dirty = false
      notice = '새 매매 전략을 만들었습니다.'
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '전략 생성에 실패했습니다.'
    }
  }

  async function selectPattern(pat) {
    if (selectedPattern?.id !== pat.id && dirty && !confirm('저장하지 않은 변경이 있습니다. 다른 전략을 불러올까요?')) return
    try {
      const res = await patternApi.get(pat.id)
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      selectedNode = { type: 'general' }
      dirty = false
      notice = ''
      error = ''
    } catch (e) {
      error = e?.message || '전략을 불러오지 못했습니다.'
    }
  }

  async function savePattern() {
    if (!workspace?.name?.trim()) {
      error = '전략 이름을 입력하세요.'
      return
    }
    if (validationIssues.length > 0) {
      error = validationIssues.join('\n')
      return
    }
    if (workspace.enableLiveTrading && !workspace.raw?.enableLiveTrading
      && !confirm('이 전략을 실시간 감시와 자동 주문에 연결합니다. 저장 후 실제 주문이 발생할 수 있습니다. 계속할까요?')) return

    saving = true
    try {
      const payload = buildPatternPayload(workspace)

      const res = await patternApi.update(selectedPattern.id, payload)
      selectedPattern = res.data
      workspace = buildWorkspace(res.data.raw)
      await loadPatterns()
      dirty = false
      notice = '매매 전략을 저장했습니다.'
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '전략 저장에 실패했습니다.'
    } finally {
      saving = false
    }
  }

  async function deletePattern(pat) {
    if (selectedPattern?.id === pat.id && dirty && !confirm('저장하지 않은 변경도 함께 사라집니다. 계속할까요?')) return
    if (!confirm(`"${pat.name}" 전략을 삭제할까요?`)) return
    try {
      await patternApi.delete(pat.id)
      if (selectedPattern?.id === pat.id) {
        selectedPattern = null
        workspace = null
        selectedNode = { type: 'general' }
      }
      await loadPatterns()
    } catch (e) {
      error = e?.message || '전략 삭제에 실패했습니다.'
    }
  }

  function selectedGroupIndex() {
    if (selectedNode.type === 'group' || selectedNode.type === 'entryRule') return selectedNode.groupIndex
    return workspace?.entryGroups?.length ? 0 : -1
  }

  function selectedExitGroupIndex() {
    if (selectedNode.type === 'exitGroup' || selectedNode.type === 'exitRule') return selectedNode.groupIndex
    return workspace?.exitGroups?.length ? 0 : -1
  }

  function addRuleToGroup(template = {}) {
    let index = selectedGroupIndex()
    if (index < 0) {
      workspace.entryGroups.push(blankGroup(`매수 상황 1`))
      index = 0
    }
    workspace.entryGroups[index].rules.push(blankRule(template))
    selectedNode = { type: 'entryRule', groupIndex: index, ruleIndex: workspace.entryGroups[index].rules.length - 1 }
    touch()
  }

  function addRuleToExitGroup(template = {}) {
    let index = selectedExitGroupIndex()
    if (index < 0) {
      workspace.exitGroups.push(blankExitGroup('매도 상황 1'))
      index = 0
    }
    workspace.exitGroups[index].rules.push(blankRule(template))
    selectedNode = { type: 'exitRule', groupIndex: index, ruleIndex: workspace.exitGroups[index].rules.length - 1 }
    touch()
  }

  function addNode(kind) {
    if (!workspace) return

    if (kind === 'group') {
      workspace.entryGroups.push(blankGroup(`매수 상황 ${workspace.entryGroups.length + 1}`))
      selectedNode = { type: 'group', groupIndex: workspace.entryGroups.length - 1 }
    } else if (kind === 'exitGroup') {
      workspace.exitGroups.push(blankExitGroup(`매도 상황 ${workspace.exitGroups.length + 1}`))
      selectedNode = { type: 'exitGroup', groupIndex: workspace.exitGroups.length - 1 }
    } else if (kind === 'weightTier') {
      workspace.weightTiers.push(blankWeightTier())
      workspace.useWeightTiers = true
      selectedNode = { type: 'weightTier', tierIndex: workspace.weightTiers.length - 1 }
    } else if (kind === 'scalingRule') {
      workspace.scalingRules.push(blankScalingRule())
      selectedNode = { type: 'scalingRule', scalingIndex: workspace.scalingRules.length - 1 }
    }

    touch()
  }

  function addTierCondition(tierIndex) {
    workspace.weightTiers[tierIndex].conditions.push(blankRule())
    selectedNode = { type: 'tierRule', tierIndex, ruleIndex: workspace.weightTiers[tierIndex].conditions.length - 1 }
    touch()
  }

  function addScalingCondition(scalingIndex) {
    workspace.scalingRules[scalingIndex].conditions.push(blankRule())
    selectedNode = { type: 'scalingRuleCondition', scalingIndex, ruleIndex: workspace.scalingRules[scalingIndex].conditions.length - 1 }
    touch()
  }

  function removeNode(node) {
    if (!workspace) return

    if (node.type === 'group') {
      workspace.entryGroups.splice(node.groupIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'entryRule') {
      workspace.entryGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      selectedNode = { type: 'group', groupIndex: node.groupIndex }
    } else if (node.type === 'exitGroup') {
      workspace.exitGroups.splice(node.groupIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'exitRule') {
      workspace.exitGroups[node.groupIndex].rules.splice(node.ruleIndex, 1)
      selectedNode = { type: 'exitGroup', groupIndex: node.groupIndex }
    } else if (node.type === 'weightTier') {
      workspace.weightTiers.splice(node.tierIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'tierRule') {
      workspace.weightTiers[node.tierIndex].conditions.splice(node.ruleIndex, 1)
      selectedNode = { type: 'weightTier', tierIndex: node.tierIndex }
    } else if (node.type === 'scalingRule') {
      workspace.scalingRules.splice(node.scalingIndex, 1)
      selectedNode = { type: 'general' }
    } else if (node.type === 'scalingRuleCondition') {
      workspace.scalingRules[node.scalingIndex].conditions.splice(node.ruleIndex, 1)
      selectedNode = { type: 'scalingRule', scalingIndex: node.scalingIndex }
    }

    touch()
  }

  function cloneValue(value) {
    return JSON.parse(JSON.stringify(value))
  }

  function moveItem(list, index, offset) {
    const next = index + offset
    if (next < 0 || next >= list.length) return index
    const [item] = list.splice(index, 1)
    list.splice(next, 0, item)
    touch()
    return next
  }

  function moveNode(node, offset) {
    if (node.type === 'group') selectedNode = { ...node, groupIndex: moveItem(workspace.entryGroups, node.groupIndex, offset) }
    else if (node.type === 'entryRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.entryGroups[node.groupIndex].rules, node.ruleIndex, offset) }
    else if (node.type === 'exitGroup') selectedNode = { ...node, groupIndex: moveItem(workspace.exitGroups, node.groupIndex, offset) }
    else if (node.type === 'exitRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.exitGroups[node.groupIndex].rules, node.ruleIndex, offset) }
    else if (node.type === 'weightTier') selectedNode = { ...node, tierIndex: moveItem(workspace.weightTiers, node.tierIndex, offset) }
    else if (node.type === 'tierRule') selectedNode = { ...node, ruleIndex: moveItem(workspace.weightTiers[node.tierIndex].conditions, node.ruleIndex, offset) }
    else if (node.type === 'scalingRule') selectedNode = { ...node, scalingIndex: moveItem(workspace.scalingRules, node.scalingIndex, offset) }
    else if (node.type === 'scalingRuleCondition') selectedNode = { ...node, ruleIndex: moveItem(workspace.scalingRules[node.scalingIndex].conditions, node.ruleIndex, offset) }
  }

  function duplicateNode(node) {
    if (node.type === 'entryRule') workspace.entryGroups[node.groupIndex].rules.splice(node.ruleIndex + 1, 0, cloneValue(workspace.entryGroups[node.groupIndex].rules[node.ruleIndex]))
    else if (node.type === 'exitRule') workspace.exitGroups[node.groupIndex].rules.splice(node.ruleIndex + 1, 0, cloneValue(workspace.exitGroups[node.groupIndex].rules[node.ruleIndex]))
    else if (node.type === 'tierRule') workspace.weightTiers[node.tierIndex].conditions.splice(node.ruleIndex + 1, 0, cloneValue(workspace.weightTiers[node.tierIndex].conditions[node.ruleIndex]))
    else if (node.type === 'scalingRuleCondition') workspace.scalingRules[node.scalingIndex].conditions.splice(node.ruleIndex + 1, 0, cloneValue(workspace.scalingRules[node.scalingIndex].conditions[node.ruleIndex]))
    else if (node.type === 'group') workspace.entryGroups.splice(node.groupIndex + 1, 0, cloneValue(workspace.entryGroups[node.groupIndex]))
    else if (node.type === 'exitGroup') workspace.exitGroups.splice(node.groupIndex + 1, 0, cloneValue(workspace.exitGroups[node.groupIndex]))
    else if (node.type === 'weightTier') workspace.weightTiers.splice(node.tierIndex + 1, 0, cloneValue(workspace.weightTiers[node.tierIndex]))
    else if (node.type === 'scalingRule') workspace.scalingRules.splice(node.scalingIndex + 1, 0, cloneValue(workspace.scalingRules[node.scalingIndex]))
    touch()
  }

  function ruleSummary(rule) {
    const indicatorLabel = indicatorLabels[rule.indicator] ?? rule.indicator
    const params = Object.entries(rule.params || {}).map(([key, value]) => `${paramKeyLabels[key] ?? key}:${value}`).join(', ')
    const compare = rule.compareIndicator
      ? ` 대비 ${indicatorLabels[rule.compareIndicator] ?? rule.compareIndicator}`
      : ` ${operatorLabels[rule.operator] ?? rule.operator} ${rule.value}`
    const meta = [rule.withinBars ? `최근 ${rule.withinBars}봉 내` : '', rule.consecutiveBars ? `${rule.consecutiveBars}봉 연속` : ''].filter(Boolean).join(' · ')
    return `${indicatorLabel}${params ? `(${params})` : ''}${compare}${meta ? ` · ${meta}` : ''}`
  }

  function displayEntryMode(value) {
    return entryModeLabels[value] ?? value
  }

  function displaySizingMode(value) {
    return sizingModeLabels[value] ?? value
  }

  function displayLogic(value) {
    return logicLabels[value] ?? value
  }

  function displayScalingDirection(value) {
    return scalingDirectionLabels[value] ?? value
  }

  function displayStopType(value) {
    return stopTypeLabels[value] ?? value
  }

  function displayTargetType(value) {
    return targetTypeLabels[value] ?? value
  }

  function tooltipFor(key) {
    return glossaryTooltips[key] ?? ''
  }

  function selectNode(node) {
    selectedNode = node
  }

  function getCurrentRule() {
    if (!workspace) return null
    if (selectedNode.type === 'entryRule') return workspace.entryGroups[selectedNode.groupIndex]?.rules[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'exitRule') return workspace.exitGroups[selectedNode.groupIndex]?.rules[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'tierRule') return workspace.weightTiers[selectedNode.tierIndex]?.conditions[selectedNode.ruleIndex] ?? null
    if (selectedNode.type === 'scalingRuleCondition') return workspace.scalingRules[selectedNode.scalingIndex]?.conditions[selectedNode.ruleIndex] ?? null
    return null
  }

  function getSelectedPreviewSummary() {
    if (!workspace) return ''
    const rule = getCurrentRule()
    if (rule) return ruleSummary(rule)
    if (selectedNode.type === 'dynamicExit') {
      return `손절 ${displayStopType(workspace.dynamicExit.stopType)} · 목표 ${displayTargetType(workspace.dynamicExit.targetType)}`
    }
    if (selectedNode.type === 'general') {
      return `${displayEntryMode(workspace.entryMode)} · 손절 ${workspace.atrStopMultiplier} ATR · 목표 ${workspace.atrTargetMultiplier} ATR`
    }
    if (selectedNode.type === 'group') {
      const group = workspace.entryGroups[selectedNode.groupIndex]
      return `${group?.label ?? '매수 상황'} · ${displayLogic(group?.logic)}`
    }
    if (selectedNode.type === 'exitGroup') {
      const group = workspace.exitGroups[selectedNode.groupIndex]
      return `${group?.label ?? '매도 상황'} · ${displayLogic(group?.logic)}`
    }
    return ''
  }

  function updateRuleField(field, value) {
    const rule = getCurrentRule()
    if (!rule) return
    rule[field] = field === 'value' || field === 'withinBars' || field === 'weight' || field === 'consecutiveBars'
      ? toNumber(value, 0)
      : value

    if (field === 'indicator') {
      rule.params = buildRuleParams(rule.indicator, rule.params)
    }
    if (field === 'compareIndicator') {
      rule.compareParams = rule.compareIndicator ? buildRuleParams(rule.compareIndicator, rule.compareParams) : {}
    }

    touch()
  }

  function addRuleMapEntry(field) {
    const rule = getCurrentRule()
    if (!rule) return
    rule[field] = { ...(rule[field] || {}), newKey: 0 }
    touch()
  }

  function updateRuleMapEntry(field, oldKey, nextKey, nextValue) {
    const rule = getCurrentRule()
    if (!rule) return
    const next = { ...(rule[field] || {}) }
    const value = nextValue ?? next[oldKey] ?? 0
    delete next[oldKey]
    next[nextKey || oldKey] = toNumber(value, 0)
    rule[field] = next
    touch()
  }

  function removeRuleMapEntry(field, key) {
    const rule = getCurrentRule()
    if (!rule) return
    const next = { ...(rule[field] || {}) }
    delete next[key]
    rule[field] = next
    touch()
  }

  function listToText(list) {
    return (list || []).join(', ')
  }

  function textToIntList(value) {
    return value.split(',').map((item) => item.trim()).filter(Boolean).map((item) => Number(item)).filter((item) => Number.isFinite(item))
  }

  function toggleListValue(list, value) {
    const next = list.includes(value) ? list.filter((item) => item !== value) : [...list, value]
    next.sort((a, b) => a - b)
    touch()
    return next
  }
</script>

<div class="flex h-full overflow-hidden">
  <aside class="flex w-80 shrink-0 flex-col border-r border-gray-800 bg-gray-950">
    <div class="border-b border-gray-800 p-6">
      <div class="mb-2 flex items-center gap-3">
        <FolderTree size={20} class="text-blue-400" />
        <h2 class="text-2xl font-bold">내 매매 전략</h2>
        <span title={tooltipFor('workspace')} class="cursor-help text-gray-500 transition hover:text-blue-300">
          <CircleHelp size={16} />
        </span>
      </div>
      <p class="text-sm text-gray-400">언제 사고, 얼마나 사고, 언제 팔지 순서대로 정합니다.</p>
    </div>

    <div class="border-b border-gray-800 p-4">
      {#if !showNewPattern}
        <button on:click={() => (showNewPattern = true)} class="flex w-full items-center justify-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
          <Plus size={16} />
          새 전략
        </button>
      {:else}
        <div class="space-y-2">
          <input bind:value={newPatternName} placeholder="전략 이름" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-sm text-white" />
          <div class="flex gap-2">
            <button on:click={createPattern} class="flex-1 rounded bg-green-600 px-3 py-2 text-sm text-white transition hover:bg-green-700">생성</button>
            <button on:click={() => (showNewPattern = false)} class="flex-1 rounded bg-gray-700 px-3 py-2 text-sm text-white transition hover:bg-gray-600">취소</button>
          </div>
        </div>
      {/if}
    </div>

    <div class="flex-1 overflow-y-auto p-4">
      <div class="mb-3 text-xs uppercase tracking-wider text-gray-500">저장한 전략</div>
      {#if loading}
        <div class="text-sm text-gray-400">불러오는 중...</div>
      {:else}
        <div class="space-y-2">
          {#each patterns as pat}
            <div class={`rounded-lg border p-3 ${selectedPattern?.id === pat.id ? 'border-blue-600 bg-blue-950/30' : 'border-gray-800 bg-gray-900'}`}>
              <button on:click={() => selectPattern(pat)} class="w-full text-left">
                <div class="font-medium text-white">{pat.name}</div>
                <div class="mt-1 text-xs text-gray-500">{pat.raw?.updatedAt ?? pat.updatedAt}</div>
              </button>
              {#if String(pat.id) !== '-1001'}
                <div class="mt-2 flex justify-end">
                  <button on:click={() => deletePattern(pat)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30" aria-label={`${pat.name} 전략 삭제`}>
                    <Trash2 size={14} />
                  </button>
                </div>
              {:else}
                <div class="mt-2 text-right text-[11px] text-blue-300">기본 예시 · 저장하면 내 전략으로 복사</div>
              {/if}
            </div>
          {/each}
        </div>
      {/if}
    </div>
    <div class="border-t border-gray-800 p-4 text-xs text-gray-500">
      가운데 매매 규칙에서 조건을 선택하면 오른쪽에서 수치를 바꿀 수 있습니다.
    </div>
  </aside>

  <section class="flex min-w-0 flex-1 flex-col border-r border-gray-800 bg-gray-900">
    {#if !workspace}
      <div class="flex h-full items-center justify-center text-gray-400">
        <div class="text-center">
          <ChevronRight size={48} class="mx-auto mb-4 opacity-50" />
          <p>왼쪽에서 전략을 선택하면 매매 규칙이 열립니다.</p>
        </div>
      </div>
    {:else}
      <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
        <div>
          <div class="flex items-center gap-2 text-sm uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('strategy')} class="cursor-help">매매 규칙</span>
            <span title={tooltipFor('strategy')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={14} />
            </span>
          </div>
          <h3 class="mt-1 text-2xl font-bold">{workspace.name || '이름 없는 전략'}</h3>
        </div>
        <div class="flex items-center gap-3">
          {#if dirty}
            <span class="rounded bg-amber-950/60 px-3 py-1 text-xs text-amber-300">미저장 변경</span>
          {/if}
          <button on:click={savePattern} disabled={saving || validationIssues.length > 0} class="flex items-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700 disabled:opacity-50">
            <Save size={16} />
            {saving ? '저장 중...' : '저장'}
          </button>
        </div>
      </div>

      <div class="flex-1 overflow-auto p-6">
        {#if error}
          <div class="mb-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
        {/if}
        {#if notice}
          <div class="mb-4 rounded-lg border border-green-700 bg-green-900/20 p-4 text-green-300">{notice}</div>
        {/if}
        {#if validationIssues.length > 0}
          <div class="mb-4 rounded-lg border border-amber-700 bg-amber-900/20 p-4 text-amber-200">
            <div class="mb-2 text-sm font-semibold">저장 전 수정할 항목</div>
            <div class="space-y-1 text-sm">
              {#each validationIssues as issue}
                <div>{issue}</div>
              {/each}
            </div>
          </div>
        {/if}

        <PatternPreview pattern={previewPattern} selectedRuleSummary={previewSelectedSummary} bind:timeFrame={workspace.timeFrame} on:timeframechange={touch} />

        <div class="mb-6 rounded-xl border border-gray-800 bg-gray-950 p-5">
          <button on:click={() => selectNode({ type: 'general' })} class={`w-full text-left ${selectedNode.type === 'general' ? 'text-blue-300' : 'text-white'}`}>
            <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
              <span title={tooltipFor('pattern')} class="cursor-help">전략 기본 설정</span>
              <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                <CircleHelp size={12} />
              </span>
            </div>
            <div class="mt-1 text-xl font-semibold">{workspace.name}</div>
            <div class="mt-2 flex flex-wrap gap-2 text-xs">
              <span class="rounded bg-gray-800 px-2 py-1">매수 시점: {displayEntryMode(workspace.entryMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">기준 봉: {timeFrameOptions.find((item) => item.value === workspace.timeFrame)?.label ?? workspace.timeFrame}</span>
              <span class="rounded bg-gray-800 px-2 py-1">주문 금액: {displaySizingMode(workspace.sizingMode)}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.isActive ? '연구 사용 중' : '연구 제외'}</span>
              <span class={`rounded px-2 py-1 ${workspace.enableLiveTrading ? 'bg-amber-900/50 text-amber-200' : 'bg-gray-800'}`}>{workspace.enableLiveTrading ? '실시간 주문 연결' : '실시간 주문 꺼짐'}</span>
              <span class="rounded bg-gray-800 px-2 py-1">{workspace.requireBullRegime ? '강세장만 허용' : '장세 무관'}</span>
            </div>
          </button>
        </div>

        <div class="space-y-5">
          <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
            <div class="mb-4 flex items-center justify-between">
              <button on:click={() => selectNode({ type: 'entryRoot' })} class="text-left">
                <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                  <span title={tooltipFor('entryGroup')} class="cursor-help">언제 살까?</span>
                  <span title={tooltipFor('entryGroup')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                    <CircleHelp size={12} />
                  </span>
                </div>
                <div class="text-lg font-semibold">매수 상황 중 {displayLogic(workspace.entryGroupsLogic)}</div>
              </button>
              <button on:click={() => addNode('group')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 상황</button>
            </div>

            <div class="space-y-3">
              {#each workspace.entryGroups as group, groupIndex}
                <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                  <div class="mb-3 flex items-center justify-between">
                    <button on:click={() => selectNode({ type: 'group', groupIndex })} class={`text-left ${selectedNode.type === 'group' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                      <div class="font-semibold">{group.label || `매수 상황 ${groupIndex + 1}`}</div>
                      <div class="mt-1 text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                    </button>
                    <div class="flex items-center gap-2">
                      <button on:click={() => addRuleToGroup({})} class="rounded bg-gray-800 px-2 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 조건</button>
                      <button title="위로" on:click={() => moveNode({ type: 'group', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                      <button title="아래로" on:click={() => moveNode({ type: 'group', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                      <button title="복제" on:click={() => duplicateNode({ type: 'group', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                      <button on:click={() => removeNode({ type: 'group', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                    </div>
                  </div>
                  <div class="space-y-2 border-l border-gray-800 pl-4">
                    {#each group.rules as rule, ruleIndex}
                      <div class="flex items-center gap-1">
                        <button on:click={() => selectNode({ type: 'entryRule', groupIndex, ruleIndex })} class={`min-w-0 flex-1 rounded border px-3 py-3 text-left text-sm transition ${selectedNode.type === 'entryRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                          <div title={tooltipFor('rule')} class="text-xs text-gray-400 cursor-help">매수 조건 {ruleIndex + 1}</div>
                          <div class="mt-1">{ruleSummary(rule)}</div>
                        </button>
                        <button title="위로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'entryRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button title="삭제" on:click={() => removeNode({ type: 'entryRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={13} /></button>
                      </div>
                    {/each}
                  </div>
                </div>
              {/each}
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'exitRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('exitRule')} class="cursor-help">언제 팔까?</span>
                    <span title={tooltipFor('exitRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">매도 상황 중 {displayLogic(workspace.exitGroupsLogic)}</div>
                </button>
                <button on:click={() => addNode('exitGroup')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매도 상황</button>
              </div>
              <div class="space-y-3">
                {#each workspace.exitGroups as group, groupIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between gap-2">
                      <button on:click={() => selectNode({ type: 'exitGroup', groupIndex })} class={`text-left ${selectedNode.type === 'exitGroup' && selectedNode.groupIndex === groupIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{group.label || `매도 상황 ${groupIndex + 1}`}</div>
                        <div class="text-xs text-gray-500">조건을 {displayLogic(group.logic)} • {group.rules.length}개</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => { selectNode({ type: 'exitGroup', groupIndex }); addRuleToExitGroup({}); }} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 매도 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'exitGroup', groupIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'exitGroup', groupIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each group.rules as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'exitRule', groupIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'exitRule' && selectedNode.groupIndex === groupIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>
                            {ruleSummary(rule)}
                          </button>
                          <button title="위로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'exitRule', groupIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'exitRule', groupIndex, ruleIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'weightRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('weightTier')} class="cursor-help">얼마나 살까?</span>
                    <span title={tooltipFor('weightTier')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">{workspace.useWeightTiers ? '사용 중' : '사용 안 함'}</div>
                </button>
                <button on:click={() => addNode('weightTier')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 매수 비중</button>
              </div>
              <div class="space-y-2">
                {#each workspace.weightTiers as tier, tierIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'weightTier', tierIndex })} class={`text-left ${selectedNode.type === 'weightTier' && selectedNode.tierIndex === tierIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{tier.label}</div>
                        <div class="text-xs text-gray-500">{displayLogic(tier.logic)} • {tier.allocationPercent}%</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addTierCondition(tierIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 적용 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'weightTier', tierIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'weightTier', tierIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each tier.conditions as rule, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'tierRule', tierIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'tierRule' && selectedNode.tierIndex === tierIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(rule)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'tierRule', tierIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'tierRule', tierIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-5">
            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center justify-between">
                <button on:click={() => selectNode({ type: 'scalingRoot' })} class="text-left">
                  <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                    <span title={tooltipFor('scalingRule')} class="cursor-help">추가 매수·분할 매도</span>
                    <span title={tooltipFor('scalingRule')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                      <CircleHelp size={12} />
                    </span>
                  </div>
                  <div class="text-lg font-semibold">설정 {workspace.scalingRules.length}개</div>
                </button>
                <button on:click={() => addNode('scalingRule')} class="rounded bg-gray-800 px-3 py-1 text-xs text-white transition hover:bg-gray-700">+ 추가 매수·매도</button>
              </div>
              <div class="space-y-2">
                {#each workspace.scalingRules as rule, scalingIndex}
                  <div class="rounded-lg border border-gray-800 bg-gray-900 p-3">
                    <div class="mb-2 flex items-center justify-between">
                      <button on:click={() => selectNode({ type: 'scalingRule', scalingIndex })} class={`text-left ${selectedNode.type === 'scalingRule' && selectedNode.scalingIndex === scalingIndex ? 'text-blue-300' : 'text-white'}`}>
                        <div class="font-semibold">{displayScalingDirection(rule.direction)}</div>
                        <div class="text-xs text-gray-500">{displayLogic(rule.logic)} • {rule.percent}% • 최대 {rule.maxCount}회</div>
                      </button>
                      <div class="flex gap-2">
                        <button on:click={() => addScalingCondition(scalingIndex)} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 실행 조건</button>
                        <button title="위로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                        <button title="아래로" on:click={() => moveNode({ type: 'scalingRule', scalingIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                        <button title="복제" on:click={() => duplicateNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                        <button on:click={() => removeNode({ type: 'scalingRule', scalingIndex })} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                      </div>
                    </div>
                    <div class="space-y-2 border-l border-gray-800 pl-3">
                      {#each rule.conditions as condition, ruleIndex}
                        <div class="flex gap-2">
                          <button on:click={() => selectNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class={`flex-1 rounded border px-3 py-2 text-left text-sm transition ${selectedNode.type === 'scalingRuleCondition' && selectedNode.scalingIndex === scalingIndex && selectedNode.ruleIndex === ruleIndex ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-950 text-gray-200 hover:border-gray-700'}`}>{ruleSummary(condition)}</button>
                          <button title="위로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, -1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowUp size={13} /></button>
                          <button title="아래로" on:click={() => moveNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex }, 1)} class="rounded p-1 text-gray-400 hover:text-white"><ArrowDown size={13} /></button>
                          <button title="복제" on:click={() => duplicateNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-gray-400 hover:text-white"><Copy size={13} /></button>
                          <button on:click={() => removeNode({ type: 'scalingRuleCondition', scalingIndex, ruleIndex })} class="rounded p-1 text-red-400 hover:bg-red-950/30"><Trash2 size={14} /></button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/each}
              </div>
            </div>

            <div class="rounded-xl border border-gray-800 bg-gray-950 p-5">
              <div class="mb-4 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
                <span title={tooltipFor('runtime')} class="cursor-help">거래 제한·안전장치</span>
                <span title={tooltipFor('runtime')} class="cursor-help text-gray-600 transition hover:text-blue-300">
                  <CircleHelp size={12} />
                </span>
              </div>
              <div class="space-y-2">
                <button on:click={() => selectNode({ type: 'timeFilter' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'timeFilter' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>매매 가능 시기</button>
                <button on:click={() => selectNode({ type: 'circuitBreaker' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'circuitBreaker' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손실 시 거래 중단</button>
                <button on:click={() => selectNode({ type: 'reentry' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'reentry' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>다시 매수하기까지 대기</button>
                <button on:click={() => selectNode({ type: 'portfolioRules' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'portfolioRules' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>보유 종목·비중 한도</button>
                <button on:click={() => selectNode({ type: 'dynamicExit' })} class={`block w-full rounded border px-4 py-3 text-left transition ${selectedNode.type === 'dynamicExit' ? 'border-blue-600 bg-blue-950/20 text-blue-100' : 'border-gray-800 bg-gray-900 text-white hover:border-gray-700'}`}>손절·목표가 계산법</button>
              </div>
            </div>
          </div>
        </div>
      </div>
    {/if}
  </section>

  <aside class="w-[30rem] shrink-0 overflow-y-auto bg-gray-950 p-6">
    {#if !workspace}
      <div class="text-gray-400">전략을 선택하면 세부 설정이 열립니다.</div>
    {:else if selectedNode.type === 'general' || selectedNode.type === 'entryRoot' || selectedNode.type === 'exitRoot' || selectedNode.type === 'weightRoot' || selectedNode.type === 'scalingRoot'}
      <div class="space-y-5">
        <div>
          <div class="mb-2 flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('pattern')} class="cursor-help">전략 세부 설정</span>
            <span title={tooltipFor('pattern')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <input bind:value={workspace.name} on:input={touch} placeholder="전략 이름" class="mb-3 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          <textarea bind:value={workspace.description} on:input={touch} rows="3" placeholder="전략 설명" class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white"></textarea>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">전략 기준 봉</div>
            <select bind:value={workspace.timeFrame} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each timeFrameOptions as option}<option value={option.value}>{option.label}</option>{/each}
            </select>
            <div class="mt-2 text-xs text-gray-500">미리보기와 백테스트에 같은 봉을 사용합니다.</div>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('entryMode')} class="mb-2 cursor-help text-gray-500">언제 주문할까요?</div>
            <select bind:value={workspace.entryMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each entryModeOptions as option}<option value={option}>{displayEntryMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div title={tooltipFor('sizingMode')} class="mb-2 cursor-help text-gray-500">주문 금액 계산법</div>
            <select bind:value={workspace.sizingMode} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each sizingModeOptions as option}<option value={option}>{displaySizingMode(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매수 상황이 여러 개라면</div>
            <select bind:value={workspace.entryGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">매도 조건이 여러 개라면</div>
            <select bind:value={workspace.exitGroupsLogic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
        </div>

        <div class="grid grid-cols-2 gap-3">
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 손절 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrStopMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">ATR 목표 배수</div>
            <input type="number" step="0.1" bind:value={workspace.atrTargetMultiplier} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">최대 보유 봉 수</div>
            <input type="number" bind:value={workspace.maxHoldingBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">기본 비중 %</div>
            <input type="number" step="1" bind:value={workspace.defaultAllocationPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">트레일링 ATR</div>
            <input type="number" step="0.1" bind:value={workspace.trailingAtr} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
          <label class="rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
            <div class="mb-2 text-gray-500">부분 익절 R</div>
            <input type="number" step="0.1" bind:value={workspace.partialProfitR} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
        </div>

        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.requireBullRegime} on:change={touch} />
          강세장일 때만 매수
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.useWeightTiers} on:change={touch} />
          상황별 매수 비중 사용
        </label>
        <label class="flex items-center gap-3 rounded border border-gray-800 bg-gray-900 p-3 text-sm text-gray-300">
          <input type="checkbox" bind:checked={workspace.isActive} on:change={touch} />
          연구·미리보기·백테스트에서 이 전략 사용
        </label>
        <label class="block rounded border border-amber-900/60 bg-amber-950/20 p-3 text-sm text-gray-300">
          <span class="flex items-center gap-3">
            <input type="checkbox" bind:checked={workspace.enableLiveTrading} on:change={touch} />
            실시간 감시와 자동 주문에 연결
          </span>
          <span class="mt-2 block text-xs leading-5 text-amber-300/80">현재 실시간 실행은 ‘일봉 + 다음 봉 시가 + 전량 청산’ 전략만 지원합니다. 추가 매수·부분 익절·분할 매도 전략은 미리보기와 백테스트에서 검증할 수 있지만 실시간 주문은 켤 수 없습니다.</span>
        </label>
      </div>
    {:else if selectedNode.type === 'group'}
      {@const group = workspace.entryGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('entryGroup')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
          {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
        </select>
        <button on:click={() => addRuleToGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매수 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'exitGroup'}
      {@const group = workspace.exitGroups[selectedNode.groupIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('exitRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매도 상황</div>
        <input bind:value={group.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <label class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">이 상황의 조건이 여러 개라면</div>
          <select bind:value={group.logic} on:change={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select>
        </label>
        <button on:click={() => addRuleToExitGroup({})} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 매도 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'weightTier'}
      {@const tier = workspace.weightTiers[selectedNode.tierIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('weightTier')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">매수 비중</div>
        <input bind:value={tier.label} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        <div class="grid grid-cols-2 gap-3">
          <label class="text-sm text-gray-400">조건 결합
            <select bind:value={tier.logic} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
              {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
            </select>
          </label>
          <label class="text-sm text-gray-400">투자 비중 (%)
            <input type="number" min="0" max="100" bind:value={tier.allocationPercent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
          </label>
        </div>
        <div class="rounded border border-blue-900/60 bg-blue-950/20 p-3 text-xs leading-5 text-blue-200">위에서부터 조건을 확인해 처음 만족한 비중 하나만 적용합니다. 순서가 결과에 영향을 줍니다.</div>
        <button on:click={() => addTierCondition(selectedNode.tierIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 적용 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'scalingRule'}
      {@const rule = workspace.scalingRules[selectedNode.scalingIndex]}
      <div class="space-y-4">
        <div title={tooltipFor('scalingRule')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">추가 매수·분할 매도</div>
        <div class="grid grid-cols-2 gap-3">
          <label class="text-sm text-gray-400">실행 종류<select bind:value={rule.direction} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each scalingDirectionOptions as option}<option value={option}>{displayScalingDirection(option)}</option>{/each}
          </select></label>
          <label class="text-sm text-gray-400">조건 결합<select bind:value={rule.logic} on:change={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each logicOptions as option}<option value={option}>{displayLogic(option)}</option>{/each}
          </select></label>
          <label class="text-sm text-gray-400">최초 매수 수량 대비 비율 (%)<input type="number" min="0" max="100" bind:value={rule.percent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
          <label class="text-sm text-gray-400">최대 실행 횟수<input type="number" min="1" bind:value={rule.maxCount} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
          <label class="col-span-2 text-sm text-gray-400">이 수익률 이상일 때만 실행 (%)<input type="number" step="0.1" bind:value={rule.minProfitPercent} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        </div>
        <button on:click={() => addScalingCondition(selectedNode.scalingIndex)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">+ 실행 조건 추가</button>
      </div>
    {:else if selectedNode.type === 'timeFilter'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">매매 가능 시기</div>
        <div class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">매수할 요일 <span class="text-xs">(선택하지 않으면 매일)</span></div>
          <div class="grid grid-cols-7 gap-2">
            {#each dayOptions as day}
              <button type="button" on:click={() => (workspace.timeFilter.allowedDaysOfWeek = toggleListValue(workspace.timeFilter.allowedDaysOfWeek, day.value))} class={`rounded border px-2 py-2 ${workspace.timeFilter.allowedDaysOfWeek.includes(day.value) ? 'border-blue-500 bg-blue-950/50 text-blue-200' : 'border-gray-700 bg-gray-900 text-gray-400'}`}>{day.label}</button>
            {/each}
          </div>
        </div>
        <div class="block text-sm text-gray-300">
          <div class="mb-2 text-gray-500">매수하지 않을 달</div>
          <div class="grid grid-cols-6 gap-2">
            {#each monthOptions as month}
              <button type="button" on:click={() => (workspace.timeFilter.blockedMonths = toggleListValue(workspace.timeFilter.blockedMonths, month))} class={`rounded border px-2 py-2 ${workspace.timeFilter.blockedMonths.includes(month) ? 'border-rose-600 bg-rose-950/40 text-rose-200' : 'border-gray-700 bg-gray-900 text-gray-400'}`}>{month}월</button>
            {/each}
          </div>
        </div>
      </div>
    {:else if selectedNode.type === 'circuitBreaker'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">손실 시 거래 중단</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">연속 손실 허용 횟수</span><input type="number" bind:value={workspace.circuitBreaker.consecutiveLossLimit} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">거래를 멈출 봉 수</span><input type="number" bind:value={workspace.circuitBreaker.cooldownBars} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">전략 최대 낙폭 %</span><input type="number" step="0.1" bind:value={workspace.circuitBreaker.maxDrawdownPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'reentry'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">다시 매수하기까지 대기</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">손실 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterLoss} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">수익 후 대기 봉 수</span><input type="number" bind:value={workspace.reentry.cooldownBarsAfterWin} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'portfolioRules'}
      <div class="space-y-4">
        <div class="text-xs uppercase tracking-wider text-gray-500">보유 종목·비중 한도</div>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">동시에 보유할 최대 종목 수</span><input type="number" bind:value={workspace.portfolioRules.maxTotalPositions} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">한 종목의 최대 비중 %</span><input type="number" step="0.1" bind:value={workspace.portfolioRules.maxSinglePositionPercent} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">하루 최대 매수 횟수</span><input type="number" bind:value={workspace.portfolioRules.maxEntriesPerDay} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
        <label class="block text-sm text-gray-300"><span class="mb-2 block text-gray-500">최대 상관계수 (백테스트)</span><input type="number" step="0.01" bind:value={workspace.portfolioRules.maxCorrelation} on:input={touch} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
      </div>
    {:else if selectedNode.type === 'dynamicExit'}
      <div class="space-y-4">
        <div title={tooltipFor('dynamicExit')} class="cursor-help text-xs uppercase tracking-wider text-gray-500">손절·목표가 계산법</div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">손절</div>
          <select bind:value={workspace.dynamicExit.stopType} on:change={(e) => setDynamicExitType('stop', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each stopTypeOptions as option}<option value={option}>{displayStopType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('stop', workspace.dynamicExit.stopType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.stopParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('stop', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
        <div class="rounded border border-gray-800 bg-gray-900 p-4">
          <div class="mb-3 text-sm font-semibold text-white">목표가</div>
          <select bind:value={workspace.dynamicExit.targetType} on:change={(e) => setDynamicExitType('target', e.currentTarget.value)} class="mb-3 w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white">
            {#each targetTypeOptions as option}<option value={option}>{displayTargetType(option)}</option>{/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            {#each getDynamicFieldConfigs('target', workspace.dynamicExit.targetType) as field}
              <label class="block text-sm text-gray-300">
                <div class="mb-2 text-gray-500">{field.label}</div>
                <input type="number" step={field.step} value={workspace.dynamicExit.targetParams[field.key] ?? field.defaultValue} on:input={(e) => updateDynamicParam('target', field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
              </label>
            {/each}
          </div>
        </div>
      </div>
    {:else}
      {@const rule = getCurrentRule()}
      {#if rule}
        <div class="space-y-4">
          <div class="flex items-center gap-2 text-xs uppercase tracking-wider text-gray-500">
            <span title={tooltipFor('ruleInspector')} class="cursor-help">선택한 조건 바꾸기</span>
            <span title={tooltipFor('ruleInspector')} class="cursor-help text-gray-600 transition hover:text-blue-300">
              <CircleHelp size={12} />
            </span>
          </div>
          <select bind:value={rule.indicator} on:change={(e) => updateRuleField('indicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>
          <div class="grid grid-cols-2 gap-3">
            <select bind:value={rule.operator} on:change={touch} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
              {#each operatorOptions as option}<option value={option}>{operatorLabels[option] ?? option}</option>{/each}
            </select>
            <label class="text-xs text-gray-400">기준값 {indicatorValueGuides[rule.indicator] ? `(${indicatorValueGuides[rule.indicator]})` : ''}<input type="number" step="0.1" bind:value={rule.value} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">최근 몇 봉 안에 한 번이라도<input type="number" min="0" bind:value={rule.withinBars} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">몇 봉 연속 만족<input type="number" min="0" bind:value={rule.consecutiveBars} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">신뢰도 계산 가중치<input type="number" min="0.1" step="0.1" bind:value={rule.weight} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" /></label>
            <label class="text-xs text-gray-400">다른 종목을 기준으로 판단<input bind:value={rule.refSymbol} on:input={touch} class="mt-1 w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 uppercase text-white" placeholder="예: SPY" /></label>
          </div>
          <div class="rounded border border-gray-800 bg-gray-900 p-3 text-xs text-gray-400">
            조건이 최근에 한 번이라도 나왔는지는 <span class="text-gray-200">최근 몇 봉 안에</span>, 계속 이어져야 한다면 <span class="text-gray-200">연속 만족</span>을 사용하세요. 두 값은 동시에 사용할 수 없습니다. 가중치는 매수 여부가 아니라 신뢰도 점수에만 반영됩니다.
          </div>
          <select bind:value={rule.compareIndicator} on:change={(e) => updateRuleField('compareIndicator', e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            <option value="">고정값과 비교</option>
            {#each indicatorPalette as section}
              <optgroup label={section.title}>
                {#each section.items as item}
                  <option value={item.indicator}>{item.label}</option>
                {/each}
              </optgroup>
            {/each}
          </select>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('params')} class="rounded bg-gray-800 px-2 py-1 text-xs text-white">+ 고급 계산값</button>
            </div>
            {#if getIndicatorFieldConfigs(rule.indicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.indicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.params?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('params', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.params, rule.indicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('params', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('params', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('params', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
          </div>

          <div class="rounded border border-gray-800 bg-gray-900 p-4">
            <div class="mb-2 flex items-center justify-between">
              <div class="text-sm font-semibold text-white">비교 지표 계산 설정</div>
              <button on:click={() => addRuleMapEntry('compareParams')} disabled={!rule.compareIndicator} class="rounded bg-gray-800 px-2 py-1 text-xs text-white disabled:opacity-40">+ 고급 계산값</button>
            </div>
            {#if rule.compareIndicator && getIndicatorFieldConfigs(rule.compareIndicator).length > 0}
              <div class="mb-3 grid grid-cols-2 gap-3">
                {#each getIndicatorFieldConfigs(rule.compareIndicator) as field}
                  <label class="block text-sm text-gray-300">
                    <div class="mb-2 text-gray-500">{field.label}</div>
                    <input type="number" step={field.step} value={rule.compareParams?.[field.key] ?? field.defaultValue} on:input={(e) => updateRuleMapEntry('compareParams', field.key, field.key, e.currentTarget.value)} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  </label>
                {/each}
              </div>
            {/if}
            <div class="space-y-2">
              {#each getExtraParamEntries(rule.compareParams, rule.compareIndicator) as [key, value]}
                <div class="grid grid-cols-[1fr,1fr,auto] gap-2">
                  <input value={key} on:input={(e) => updateRuleMapEntry('compareParams', key, e.currentTarget.value, value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <input type="number" step="0.1" value={value} on:input={(e) => updateRuleMapEntry('compareParams', key, key, e.currentTarget.value)} class="rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
                  <button on:click={() => removeRuleMapEntry('compareParams', key)} class="rounded p-1 text-red-400 transition hover:bg-red-950/30"><Trash2 size={14} /></button>
                </div>
              {/each}
            </div>
            {#if !rule.compareIndicator}
              <div class="text-xs text-gray-500">비교 지표를 선택하면 해당 지표의 계산 설정이 여기 표시됩니다.</div>
            {/if}
          </div>
        </div>
      {/if}
    {/if}
  </aside>
</div>

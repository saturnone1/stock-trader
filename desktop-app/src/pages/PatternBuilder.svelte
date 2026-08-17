<script>
  import { onMount } from 'svelte'
  import { ChevronRight, CircleHelp, Save } from 'lucide-svelte'
  import { metadataApi, patternApi } from '../api/endpoints'
  import PatternPreview from '../lib/PatternPreview.svelte'
  import { collectPatternValidationIssues } from '../features/pattern-builder/patternValidation'
  import { createPatternWorkspaceModel } from '../features/pattern-builder/patternWorkspace'
  import { createPatternEditorCommands } from '../features/pattern-builder/patternEditorCommands'
  import PatternWorkspaceSidebar from '../features/pattern-builder/PatternWorkspaceSidebar.svelte'
  import PatternStrategyTree from '../features/pattern-builder/PatternStrategyTree.svelte'
  import PatternRuleInspector from '../features/pattern-builder/PatternRuleInspector.svelte'

  const workspaceModel = createPatternWorkspaceModel()
  const editorCommands = createPatternEditorCommands({
    blankRule: (...args) => workspaceModel.blankRule(...args),
    blankGroup: (...args) => workspaceModel.blankGroup(...args),
    blankExitGroup: (...args) => workspaceModel.blankExitGroup(...args),
    blankWeightTier: (...args) => workspaceModel.blankWeightTier(...args),
    blankScalingRule: (...args) => workspaceModel.blankScalingRule(...args)
  })

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

  function applyEditorCommand(result) {
    if (!result?.changed) return
    workspace = result.workspace
    selectedNode = result.selectedNode
    dirty = true
  }

  function addRuleToGroup(template = {}) {
    applyEditorCommand(editorCommands.addEntryRule(workspace, selectedNode, template))
  }

  function addRuleToExitGroup(template = {}) {
    applyEditorCommand(editorCommands.addExitRule(workspace, selectedNode, template))
  }

  function addNode(kind) {
    applyEditorCommand(editorCommands.addNode(workspace, selectedNode, kind))
  }

  function addTierCondition(tierIndex) {
    applyEditorCommand(editorCommands.addTierCondition(workspace, selectedNode, tierIndex))
  }

  function addScalingCondition(scalingIndex) {
    applyEditorCommand(editorCommands.addScalingCondition(workspace, selectedNode, scalingIndex))
  }

  function removeNode(node) {
    applyEditorCommand(editorCommands.removeNode(workspace, selectedNode, node))
  }

  function moveNode(node, offset) {
    applyEditorCommand(editorCommands.moveNode(workspace, selectedNode, node, offset))
  }

  function duplicateNode(node) {
    applyEditorCommand(editorCommands.duplicateNode(workspace, selectedNode, node))
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
  <PatternWorkspaceSidebar
    {patterns}
    {selectedPattern}
    {loading}
    {tooltipFor}
    {createPattern}
    {selectPattern}
    {deletePattern}
    bind:showNewPattern
    bind:newPatternName
  />
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

        <PatternStrategyTree
          {workspace}
          {selectedNode}
          {timeFrameOptions}
          {tooltipFor}
          {selectNode}
          {displayEntryMode}
          {displaySizingMode}
          {displayLogic}
          {displayScalingDirection}
          {ruleSummary}
          {addNode}
          {addRuleToGroup}
          {addRuleToExitGroup}
          {moveNode}
          {duplicateNode}
          {removeNode}
          {addTierCondition}
          {addScalingCondition}
        />
      </div>
    {/if}
  </section>

  <PatternRuleInspector
    bind:workspace
    {selectedNode}
    {tooltipFor}
    {touch}
    {timeFrameOptions}
    {entryModeOptions}
    {sizingModeOptions}
    {logicOptions}
    {scalingDirectionOptions}
    {stopTypeOptions}
    {targetTypeOptions}
    {dayOptions}
    {monthOptions}
    {indicatorPalette}
    {operatorOptions}
    {operatorLabels}
    {indicatorValueGuides}
    {displayEntryMode}
    {displaySizingMode}
    {displayLogic}
    {displayScalingDirection}
    {displayStopType}
    {displayTargetType}
    {addRuleToGroup}
    {addRuleToExitGroup}
    {addTierCondition}
    {addScalingCondition}
    {toggleListValue}
    {setDynamicExitType}
    {getDynamicFieldConfigs}
    {updateDynamicParam}
    {getCurrentRule}
    {updateRuleField}
    {getIndicatorFieldConfigs}
    {getExtraParamEntries}
    {addRuleMapEntry}
    {updateRuleMapEntry}
    {removeRuleMapEntry}
  />
</div>

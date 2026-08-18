<script>
  import { onMount } from 'svelte'
  import { ChevronRight, CircleHelp, Save } from 'lucide-svelte'
  import { metadataApi, patternApi } from '../api/endpoints'
  import PatternPreview from '../lib/PatternPreview.svelte'
  import { collectPatternValidationIssues } from '../features/pattern-builder/patternValidation'
  import { createPatternWorkspaceModel } from '../features/pattern-builder/patternWorkspace'
  import { createPatternEditorCommands } from '../features/pattern-builder/patternEditorCommands'
  import { emptyPatternMetadata, projectPatternMetadata } from '../features/pattern-builder/patternMetadata'
  import { buildPatternPreviewModel, findSelectedRule, summarizeRule } from '../features/pattern-builder/patternPreviewModel'
  import { dayOptions, glossaryTooltips, monthOptions, operatorLabels, paramKeyLabels } from '../features/pattern-builder/patternBuilderUiCatalog'
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

  let builderMetadata = emptyPatternMetadata()
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
    builderMetadata = projectPatternMetadata(metadata)
    workspaceModel.configure({
      indicatorFieldConfigs: builderMetadata.indicatorFieldConfigs,
      dynamicExitFieldConfigs: builderMetadata.dynamicExitFieldConfigs
    })
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
    indicatorSet: builderMetadata.indicatorSet,
    positiveParamKeys: builderMetadata.positiveParamKeys,
    paramKeyLabels,
    liveStrategyConstraints: builderMetadata.liveStrategyConstraints
  }) : []
  $: previewModel = buildPatternPreviewModel(workspace, selectedNode, buildPatternPayload, {
    indicators: builderMetadata.indicatorLabels,
    parameters: paramKeyLabels,
    operators: operatorLabels,
    entryModes: builderMetadata.entryModeLabels,
    logicModes: builderMetadata.logicLabels,
    stopTypes: builderMetadata.stopTypeLabels,
    targetTypes: builderMetadata.targetTypeLabels
  })

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
    return summarizeRule(rule, {
      indicators: builderMetadata.indicatorLabels,
      parameters: paramKeyLabels,
      operators: operatorLabels
    })
  }

  function displayEntryMode(value) {
    return builderMetadata.entryModeLabels[value] ?? value
  }

  function displaySizingMode(value) {
    return builderMetadata.sizingModeLabels[value] ?? value
  }

  function displayLogic(value) {
    return builderMetadata.logicLabels[value] ?? value
  }

  function displayScalingDirection(value) {
    return builderMetadata.scalingDirectionLabels[value] ?? value
  }

  function displayStopType(value) {
    return builderMetadata.stopTypeLabels[value] ?? value
  }

  function displayTargetType(value) {
    return builderMetadata.targetTypeLabels[value] ?? value
  }

  function tooltipFor(key) {
    return glossaryTooltips[key] ?? ''
  }

  function selectNode(node) {
    selectedNode = node
  }

  function getCurrentRule() {
    return findSelectedRule(workspace, selectedNode)
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

        <PatternPreview pattern={previewModel.pattern} selectedRuleSummary={previewModel.selectedRuleSummary} bind:timeFrame={workspace.timeFrame} on:timeframechange={touch} />

        <PatternStrategyTree
          {workspace}
          {selectedNode}
          timeFrameOptions={builderMetadata.timeFrameOptions}
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
    timeFrameOptions={builderMetadata.timeFrameOptions}
    entryModeOptions={builderMetadata.entryModeOptions}
    sizingModeOptions={builderMetadata.sizingModeOptions}
    logicOptions={builderMetadata.logicOptions}
    scalingDirectionOptions={builderMetadata.scalingDirectionOptions}
    stopTypeOptions={builderMetadata.stopTypeOptions}
    targetTypeOptions={builderMetadata.targetTypeOptions}
    {dayOptions}
    {monthOptions}
    indicatorPalette={builderMetadata.indicatorPalette}
    operatorOptions={builderMetadata.operatorOptions}
    {operatorLabels}
    indicatorValueGuides={builderMetadata.indicatorValueGuides}
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

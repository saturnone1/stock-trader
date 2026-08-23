<script>
  import { onMount } from 'svelte'
  import { RotateCcw } from 'lucide-svelte'
  import { metadataApi, optimizationApi, patternApi } from '../api/endpoints'
  import OptimizationJobForm from '../features/optimization/OptimizationJobForm.svelte'
  import OptimizationJobList from '../features/optimization/OptimizationJobList.svelte'
  import {
    buildOptimizationJob,
    entryRules,
    exitRules,
    preferredRuleIndex,
    projectOptimizationRankingMetadata,
    selectableRules,
    toNumber
  } from '../features/optimization/optimizationModel'

  export let initialContext = null

  let rankOptions = []
  let defaultRankBy = ''
  let optimizationExecution = null

  let timeFrameOptions = []
  let dataSourceOptions = [['', '기본 설정']]

  let entryModeLabelMap = {}
  let sizingModeLabelMap = {}
  let patterns = []
  let jobs = []
  let jobDetails = {}
  let jobResults = {}
  let expandedId = null
  let loading = true
  let creating = false
  let refreshing = false
  let error = ''
  let refreshInterval

  let form = {
    patternId: '',
    symbolsText: 'SPY, QQQ, TQQQ',
    from: '',
    to: '',
    timeFrame: 'Daily',
    dataSource: '',
    rankBy: '',
    maxResults: 10,
    maxCombinations: 500,
    oosPercent: 0.25,
    jobName: '',
    priority: 0,
    chunkSize: 200,
    maxDurationHours: '',
    maxTestedCombinations: '',
    topResultsToKeep: 50,
    continuousMode: false,
    autoApplyBestResult: false,
    autoApplyMinTrades: 10,
    tuningFocus: 'entry',
    timingFocusMode: true,
    selectedEntryRuleIndex: '',
    selectedExitRuleIndex: '',
    entryPeriodValuesText: '10, 20, 30',
    exitPeriodValuesText: '5, 10, 20',
    sweepEntryLogic: false,
    sweepExitLogic: false,
    sweepRequireBullRegime: false,
    sweepEntryMode: false,
    sweepSizingMode: false,
    includeRiskExitAxes: false,
    atrStopMin: 1.5,
    atrStopMax: 3,
    atrStopStep: 0.5,
    atrTargetMin: 2,
    atrTargetMax: 5,
    atrTargetStep: 0.5,
    maxHoldingMin: 5,
    maxHoldingMax: 20,
    maxHoldingStep: 5,
    trailingAtrMin: 0,
    trailingAtrMax: 2,
    trailingAtrStep: 0.5,
    partialProfitMin: 0,
    partialProfitMax: 3,
    partialProfitStep: 0.5,
    defaultAllocationMin: 30,
    defaultAllocationMax: 100,
    defaultAllocationStep: 10,
    entryLogicOptions: [],
    exitLogicOptions: [],
    requireBullRegimeOptions: [true, false],
    entryModeOptions: [],
    sizingModeOptions: []
  }

  // Keep these projections reactive to both the asynchronously loaded catalog and
  // the selected strategy. Calling a zero-argument helper from markup hides those
  // dependencies from Svelte and can leave the tuning choices permanently empty.
  $: tuningPattern = patterns.find((item) => String(item.id) === String(form.patternId))
  $: entryRuleOptions = tuningPattern?.raw ? selectableRules(entryRules(tuningPattern.raw)) : []
  $: exitRuleOptions = tuningPattern?.raw ? selectableRules(exitRules(tuningPattern.raw)) : []

  onMount(() => {
    initialize()
    refreshInterval = setInterval(loadJobs, 4000)
    return () => clearInterval(refreshInterval)
  })

  async function initialize() {
    loading = true
    await Promise.all([loadMetadata(), loadPatterns(), loadJobs()])
    applyInitialContext()
    applyTuningFocus(form.tuningFocus)
    loading = false
  }

  function applyInitialContext() {
    if (!initialContext) return
    if (initialContext.patternId != null && patterns.some((item) => String(item.id) === String(initialContext.patternId))) {
      form.patternId = initialContext.patternId
    }
    if (initialContext.symbolsText) form.symbolsText = initialContext.symbolsText
    if (initialContext.from) form.from = initialContext.from.slice(0, 10)
    if (initialContext.to) form.to = initialContext.to.slice(0, 10)
    if (initialContext.timeFrame) form.timeFrame = initialContext.timeFrame
    if (initialContext.dataSource != null) form.dataSource = initialContext.dataSource
    const pattern = currentPattern()
    if (pattern) form.jobName = `${pattern.name} 수치 다듬기`
  }

  async function loadMetadata() {
    try {
      const metadata = await metadataApi.getStrategyBuilder()
      const rankingMetadata = projectOptimizationRankingMetadata(metadata)
      optimizationExecution = metadata?.optimizationExecution ?? null
      rankOptions = rankingMetadata.rankOptions
      defaultRankBy = rankingMetadata.defaultRankBy
      if (!rankOptions.some(([value]) => value === form.rankBy)) form.rankBy = defaultRankBy
      timeFrameOptions = (metadata?.timeFrames ?? []).map((item) => [item.value, item.displayName])
      dataSourceOptions = [['', '기본 설정'], ...(metadata?.dataProviders ?? []).map((item) => [item.value, item.displayName])]
      entryModeLabelMap = Object.fromEntries((metadata?.entryModes ?? []).map((item) => [item.code, item.displayName]))
      sizingModeLabelMap = Object.fromEntries((metadata?.sizingModes ?? []).map((item) => [item.code, item.displayName]))
      form.entryModeOptions = (metadata?.entryModes ?? []).map((item) => item.code)
      form.sizingModeOptions = (metadata?.sizingModes ?? []).map((item) => item.code)
      form.entryLogicOptions = (metadata?.logicModes ?? []).map((item) => item.code)
      form.exitLogicOptions = [...form.entryLogicOptions].reverse()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '시간축·데이터 공급자 정보를 불러오지 못했습니다.'
    }
  }

  async function loadPatterns() {
    try {
      const res = await patternApi.list()
      patterns = res.data ?? []
      if (!form.patternId && patterns.length > 0) {
        form.patternId = patterns[0].id
        form.jobName = `${patterns[0].name} 수치 다듬기`
        syncTimingDefaults(patterns[0])
      }
    } catch (e) {
      error = e?.message || '패턴 목록을 불러오지 못했습니다.'
    }
  }

  async function loadJobs() {
    refreshing = true
    try {
      const res = await optimizationApi.list()
      jobs = res.data ?? []
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '최적화 작업 목록을 불러오지 못했습니다.'
    } finally {
      refreshing = false
    }
  }

  function syncTimingDefaults(pattern) {
    if (!pattern?.raw) return
    if (form.selectedEntryRuleIndex === '') form.selectedEntryRuleIndex = preferredRuleIndex(entryRules(pattern.raw))
    if (form.selectedExitRuleIndex === '') form.selectedExitRuleIndex = preferredRuleIndex(exitRules(pattern.raw))
  }

  function selectPattern() {
    const pattern = currentPattern()
    form.selectedEntryRuleIndex = ''
    form.selectedExitRuleIndex = ''
    if (!pattern) return
    form.jobName = `${pattern.name} 수치 다듬기`
    syncTimingDefaults(pattern)
    applyTuningFocus(form.tuningFocus)
  }

  function applyTuningFocus(focus) {
    form.tuningFocus = focus
    form.timingFocusMode = focus !== 'risk'
    form.includeRiskExitAxes = focus === 'risk'
    form.sweepEntryLogic = false
    form.sweepExitLogic = false
    form.sweepRequireBullRegime = false
    form.sweepEntryMode = false
    form.sweepSizingMode = false
    const pattern = currentPattern()
    form.selectedEntryRuleIndex = focus === 'entry' && pattern?.raw ? preferredRuleIndex(entryRules(pattern.raw)) : ''
    form.selectedExitRuleIndex = focus === 'exit' && pattern?.raw ? preferredRuleIndex(exitRules(pattern.raw)) : ''
    form = { ...form }
  }

  function currentPattern() {
    return patterns.find((item) => String(item.id) === String(form.patternId))
  }

  async function toggleExpand(jobId) {
    expandedId = expandedId === jobId ? null : jobId
    if (expandedId === jobId) {
      await loadJobDetail(jobId)
    }
  }

  async function loadJobDetail(jobId) {
    try {
      const [detailRes, resultsRes] = await Promise.all([
        optimizationApi.get(String(jobId)),
        optimizationApi.results(String(jobId), 20)
      ])
      jobDetails = { ...jobDetails, [jobId]: detailRes.data }
      jobResults = { ...jobResults, [jobId]: resultsRes.data ?? [] }
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '작업 상세 정보를 불러오지 못했습니다.'
    }
  }

  async function createJob() {
    const pattern = currentPattern()
    const request = buildOptimizationJob(form, pattern)
    if (request.error) {
      error = request.error
      return
    }

    creating = true
    try {
      await optimizationApi.create(request.payload)
      await loadJobs()
      error = ''
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '최적화 작업 생성에 실패했습니다.'
    } finally {
      creating = false
    }
  }

  async function changeJobState(job, action) {
    try {
      if (action === 'pause') await optimizationApi.pause(job.id)
      if (action === 'resume') await optimizationApi.resume(job.id)
      if (action === 'cancel') await optimizationApi.cancel(job.id)
      if (expandedId === job.id) await loadJobDetail(job.id)
      await loadJobs()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '작업 상태 변경에 실패했습니다.'
    }
  }

  async function removeJob(job) {
    if (!confirm(`"${job.name}" 작업을 삭제할까요?`)) return
    try {
      await optimizationApi.remove(job.id)
      if (expandedId === job.id) expandedId = null
      await loadJobs()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '작업 삭제에 실패했습니다.'
    }
  }

  async function saveJobSettings(jobId) {
    const detail = jobDetails[jobId]
    if (!detail) return
    try {
      await optimizationApi.updateSettings(String(jobId), {
        autoApplyBestResult: !!detail.autoApplyBestResult,
        autoApplyMinTrades: toNumber(detail.autoApplyMinTrades, 10)
      })
      await loadJobDetail(jobId)
      await loadJobs()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '자동 반영 설정 저장에 실패했습니다.'
    }
  }

  async function applyResult(jobId, resultId) {
    try {
      await optimizationApi.applyResult(String(jobId), resultId)
      await loadJobDetail(jobId)
      await loadJobs()
    } catch (e) {
      error = e?.response?.data?.error || e?.message || '결과 반영에 실패했습니다.'
    }
  }
</script>

<div class="flex-1 overflow-auto">
  <div class="p-8 space-y-8">
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-3xl font-bold">전략 수치 다듬기</h2>
        <p class="mt-2 text-sm text-gray-400">검증한 전략에서 한 영역만 선택해 현재값 주변의 후보를 보수적으로 비교합니다.</p>
      </div>
      <button on:click={loadJobs} class="flex items-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
        <RotateCcw size={16} />
        {refreshing ? '새로고침 중...' : '새로고침'}
      </button>
    </div>

    {#if error}
      <div class="rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
    {/if}

    <OptimizationJobForm
      {form}
      {patterns}
      {timeFrameOptions}
      {dataSourceOptions}
      {rankOptions}
      execution={optimizationExecution}
      {entryRuleOptions}
      {exitRuleOptions}
      baseline={initialContext?.baseline}
      {creating}
      {loading}
      onPatternChange={selectPattern}
      onFocusChange={applyTuningFocus}
      onCreate={createJob}
    />

    <OptimizationJobList
      {jobs}
      {loading}
      {expandedId}
      {jobDetails}
      {jobResults}
      entryModeLabels={entryModeLabelMap}
      sizingModeLabels={sizingModeLabelMap}
      onToggle={toggleExpand}
      onStateChange={changeJobState}
      onRemove={removeJob}
      onSaveSettings={saveJobSettings}
      onApplyResult={applyResult}
    />
  </div>
</div>

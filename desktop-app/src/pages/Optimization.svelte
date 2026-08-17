<script>
  import { onMount } from 'svelte'
  import { ChevronDown, Pause, Play, RotateCcw, Save, Trash2, Zap } from 'lucide-svelte'
  import { metadataApi, optimizationApi, patternApi } from '../api/endpoints'
  import FinancialFactorBuilder from '../lib/FinancialFactorBuilder.svelte'
  import UniverseBuilder from '../lib/UniverseBuilder.svelte'

  const rankOptions = [
    ['sortinoRatio', '소르티노 비율'],
    ['sharpeRatio', '샤프 비율'],
    ['totalReturn', '총 수익률'],
    ['calmarRatio', '칼마 비율'],
    ['profitFactor', '프로핏 팩터'],
    ['winRate', '승률']
  ]

  let timeFrameOptions = []
  let dataSourceOptions = [['', '기본 설정']]

  let entryModeLabelMap = {}
  let sizingModeLabelMap = {}
  let logicOptionValues = []

  const yesNoOptions = [
    [true, '사용'],
    [false, '사용 안 함']
  ]

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
    rankBy: 'sortinoRatio',
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
    timingFocusMode: true,
    selectedEntryRuleIndex: '',
    selectedExitRuleIndex: '',
    entryPeriodValuesText: '10, 20, 30',
    exitPeriodValuesText: '5, 10, 20',
    sweepEntryLogic: false,
    sweepExitLogic: false,
    sweepRequireBullRegime: true,
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
    entryLogicOptions: ['AND', 'OR'],
    exitLogicOptions: ['OR', 'AND'],
    requireBullRegimeOptions: [true, false],
    entryModeOptions: ['CurrentClose', 'NextOpen'],
    sizingModeOptions: ['FixedRisk', 'Kelly', 'HalfKelly']
  }

  onMount(() => {
    initialize()
    refreshInterval = setInterval(loadJobs, 4000)
    return () => clearInterval(refreshInterval)
  })

  async function initialize() {
    loading = true
    await Promise.all([loadMetadata(), loadPatterns(), loadJobs()])
    loading = false
  }

  async function loadMetadata() {
    try {
      const metadata = await metadataApi.getStrategyBuilder()
      timeFrameOptions = (metadata?.timeFrames ?? []).map((item) => [item.value, item.displayName])
      dataSourceOptions = [['', '기본 설정'], ...(metadata?.dataProviders ?? []).map((item) => [item.value, item.displayName])]
      entryModeLabelMap = Object.fromEntries((metadata?.entryModes ?? []).map((item) => [item.code, item.displayName]))
      sizingModeLabelMap = Object.fromEntries((metadata?.sizingModes ?? []).map((item) => [item.code, item.displayName]))
      logicOptionValues = (metadata?.logicModes ?? []).map((item) => [item.code, item.displayName])
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
        form.jobName = `${patterns[0].name} 타이밍 최적화`
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

  function toNumber(value, fallback = 0) {
    const parsed = Number(value)
    return Number.isFinite(parsed) ? parsed : fallback
  }

  function parseSymbols(text) {
    return text.split(',').map((item) => item.trim().toUpperCase()).filter(Boolean)
  }

  function buildRange(min, max, step) {
    return {
      min: toNumber(min, 0),
      max: toNumber(max, 0),
      step: toNumber(step, 1)
    }
  }

  function safeParse(value, fallback) {
    try {
      return value ? JSON.parse(value) : fallback
    } catch {
      return fallback
    }
  }

  function parseNumberList(text) {
    return [...new Set(
      String(text ?? '')
        .split(',')
        .map((item) => Number(item.trim()))
        .filter((value) => Number.isFinite(value) && value > 0)
    )].sort((a, b) => a - b)
  }

  function flattenEntryRules(rawPattern) {
    const groups = safeParse(rawPattern?.entryGroupsJson, [])
    if (Array.isArray(groups) && groups.length > 0) {
      return groups.flatMap((group) => group.rules ?? group.Rules ?? [])
    }
    return safeParse(rawPattern?.entryRulesJson, [])
  }

  function extractExitRules(rawPattern) {
    return safeParse(rawPattern?.exitRulesJson, [])
  }

  function ruleLabel(rule, index) {
    const indicator = rule?.indicator ?? rule?.Indicator ?? '규칙'
    const params = rule?.params ?? rule?.Params ?? {}
    const period = params.period != null ? ` · 기간 ${params.period}` : ''
    const refSymbol = (rule?.refSymbol ?? rule?.RefSymbol ?? '').trim()
    return `${index + 1}. ${refSymbol ? `${refSymbol} ` : ''}${indicator}${period}`
  }

  function timingEntryRules() {
    const pattern = currentPattern()
    if (!pattern?.raw) return []
    return flattenEntryRules(pattern.raw).map((rule, index) => ({ index, label: ruleLabel(rule, index), rule }))
  }

  function timingExitRules() {
    const pattern = currentPattern()
    if (!pattern?.raw) return []
    return extractExitRules(pattern.raw).map((rule, index) => ({ index, label: ruleLabel(rule, index), rule }))
  }

  function syncTimingDefaults(pattern) {
    if (!pattern?.raw) return
    const entryRules = flattenEntryRules(pattern.raw)
    const exitRules = extractExitRules(pattern.raw)

    if (entryRules.length > 0 && form.selectedEntryRuleIndex === '') {
      const preferredEntryIndex = entryRules.findIndex((rule) => (rule?.params ?? rule?.Params ?? {}).period != null)
      form.selectedEntryRuleIndex = String(preferredEntryIndex >= 0 ? preferredEntryIndex : 0)
    }

    if (exitRules.length > 0 && form.selectedExitRuleIndex === '') {
      const preferredExitIndex = exitRules.findIndex((rule) => (rule?.params ?? rule?.Params ?? {}).period != null)
      form.selectedExitRuleIndex = String(preferredExitIndex >= 0 ? preferredExitIndex : 0)
    }
  }

  function estimatedCombinationCount() {
    const entryPeriods = parseNumberList(form.entryPeriodValuesText)
    const exitPeriods = parseNumberList(form.exitPeriodValuesText)
    let total = 1

    if (form.timingFocusMode) {
      if (form.selectedEntryRuleIndex !== '') total *= Math.max(entryPeriods.length, 1)
      if (form.selectedExitRuleIndex !== '') total *= Math.max(exitPeriods.length, 1)
      if (form.sweepEntryLogic) total *= Math.max(form.entryLogicOptions.length, 1)
      if (form.sweepExitLogic) total *= Math.max(form.exitLogicOptions.length, 1)
      if (form.sweepRequireBullRegime) total *= Math.max(form.requireBullRegimeOptions.length, 1)
      if (form.sweepEntryMode) total *= Math.max(form.entryModeOptions.length, 1)
      if (form.sweepSizingMode) total *= Math.max(form.sizingModeOptions.length, 1)
    }

    if (form.includeRiskExitAxes) {
      const lengths = [
        buildRange(form.atrStopMin, form.atrStopMax, form.atrStopStep),
        buildRange(form.atrTargetMin, form.atrTargetMax, form.atrTargetStep),
        buildRange(form.maxHoldingMin, form.maxHoldingMax, form.maxHoldingStep),
        buildRange(form.trailingAtrMin, form.trailingAtrMax, form.trailingAtrStep),
        buildRange(form.partialProfitMin, form.partialProfitMax, form.partialProfitStep),
        buildRange(form.defaultAllocationMin, form.defaultAllocationMax, form.defaultAllocationStep)
      ].map((range) => {
        const step = Number(range.step || 1)
        if (step <= 0) return 1
        return Math.max(1, Math.floor(((Number(range.max) - Number(range.min)) / step) + 1))
      })
      total *= lengths.reduce((acc, value) => acc * value, 1)
    }

    return total
  }

  function formatDate(dateStr) {
    if (!dateStr) return '-'
    return new Date(dateStr).toLocaleString('ko-KR')
  }

  function formatPercent(value, digits = 1) {
    return `${(Number(value ?? 0) * 100).toFixed(digits)}%`
  }

  function formatDuration(seconds) {
    if (seconds == null || !Number.isFinite(seconds)) return '-'
    const total = Math.max(0, Math.round(seconds))
    const h = Math.floor(total / 3600)
    const m = Math.floor((total % 3600) / 60)
    const s = total % 60
    if (h > 0) return `${h}시간 ${m}분`
    if (m > 0) return `${m}분 ${s}초`
    return `${s}초`
  }

  function statusClass(status) {
    return {
      Pending: 'bg-yellow-950/60 text-yellow-300',
      Running: 'bg-blue-950/60 text-blue-300',
      Paused: 'bg-purple-950/60 text-purple-300',
      Completed: 'bg-green-950/60 text-green-300',
      Failed: 'bg-red-950/60 text-red-300',
      Cancelled: 'bg-gray-800 text-gray-300'
    }[status] ?? 'bg-gray-800 text-gray-300'
  }

  function currentPattern() {
    return patterns.find((item) => String(item.id) === String(form.patternId))
  }

  function summaryParams(result) {
    const params = result?.params ?? {}
    const ruleOverrides = params.ruleOverrides ?? params.RuleOverrides ?? []
    const entryPeriod = ruleOverrides.find((entry) => (entry.scope ?? entry.Scope ?? 'Entry') === 'Entry' && (entry.paramKey ?? entry.ParamKey) === 'period')?.value
    const exitPeriod = ruleOverrides.find((entry) => (entry.scope ?? entry.Scope ?? 'Entry') === 'Exit' && (entry.paramKey ?? entry.ParamKey) === 'period')?.value
    return [
      entryPeriod != null ? `진입기간 ${entryPeriod}` : '',
      exitPeriod != null ? `청산기간 ${exitPeriod}` : '',
      params.atrStopMultiplier != null ? `손절 ${params.atrStopMultiplier}` : '',
      params.atrTargetMultiplier != null ? `목표 ${params.atrTargetMultiplier}` : '',
      params.maxHoldingBars != null ? `보유 ${params.maxHoldingBars}봉` : '',
      params.defaultAllocationPercent != null ? `기본비중 ${params.defaultAllocationPercent}%` : '',
      params.entryLogic ? `진입 ${params.entryLogic}` : '',
      params.exitLogic ? `청산 ${params.exitLogic}` : '',
      params.entryMode ? `진입방식 ${entryModeLabelMap[params.entryMode] ?? params.entryMode}` : '',
      params.sizingMode ? `사이징 ${sizingModeLabelMap[params.sizingMode] ?? params.sizingMode}` : ''
    ].filter(Boolean).join(' · ')
  }

  function median(values) {
    const sorted = [...values].filter((value) => Number.isFinite(value)).sort((a, b) => a - b)
    if (!sorted.length) return null
    const middle = Math.floor(sorted.length / 2)
    return sorted.length % 2 === 0
      ? (sorted[middle - 1] + sorted[middle]) / 2
      : sorted[middle]
  }

  function getResultBenchmarks(results) {
    return {
      medianTradeCount: median(results.map((item) => Number(item.tradeCount ?? 0))),
      medianDrawdown: median(results.map((item) => Number(item.maxDrawdown ?? 0))),
      medianProfitFactor: median(results.map((item) => Number(item.profitFactor ?? 0))),
      medianReturnPerTrade: median(results.map((item) => Number(item.totalReturn ?? 0) / Math.max(1, Number(item.tradeCount ?? 0))))
    }
  }

  function getResultInsights(result, results) {
    const benchmarks = getResultBenchmarks(results)
    const tradeCount = Number(result.tradeCount ?? 0)
    const drawdown = Number(result.maxDrawdown ?? 0)
    const profitFactor = Number(result.profitFactor ?? 0)
    const returnPerTrade = Number(result.totalReturn ?? 0) / Math.max(1, tradeCount)
    const oosSharpeGap = result.oosSharpeRatio == null ? null : Math.abs(Number(result.sharpeRatio ?? 0) - Number(result.oosSharpeRatio ?? 0))
    const oosReturnGap = result.oosTotalReturn == null ? null : Math.abs(Number(result.totalReturn ?? 0) - Number(result.oosTotalReturn ?? 0))

    return [
      {
        label: '낙폭 절감',
        value: benchmarks.medianDrawdown == null ? formatPercent(drawdown, 2) : formatSignedPercent(benchmarks.medianDrawdown - drawdown, 2),
        tone: benchmarks.medianDrawdown == null || drawdown <= benchmarks.medianDrawdown ? 'text-green-300' : 'text-red-300',
        description: benchmarks.medianDrawdown == null ? '현재 낙폭' : '중앙값 대비 개선'
      },
      {
        label: '거래 수 절감',
        value: benchmarks.medianTradeCount == null ? `${tradeCount}` : `${(benchmarks.medianTradeCount - tradeCount) > 0 ? '+' : ''}${Math.round(benchmarks.medianTradeCount - tradeCount)}`,
        tone: benchmarks.medianTradeCount == null || tradeCount <= benchmarks.medianTradeCount ? 'text-blue-200' : 'text-red-300',
        description: benchmarks.medianTradeCount == null ? '현재 거래 수' : '중앙값 대비 감소'
      },
      {
        label: '휩소 억제 추정',
        value: benchmarks.medianReturnPerTrade == null ? formatPercent(returnPerTrade, 2) : formatSignedPercent(returnPerTrade - benchmarks.medianReturnPerTrade, 2),
        tone: (benchmarks.medianTradeCount == null || tradeCount <= benchmarks.medianTradeCount) && (benchmarks.medianProfitFactor == null || profitFactor >= benchmarks.medianProfitFactor) ? 'text-emerald-300' : 'text-red-300',
        description: `거래당 수익 ${formatPercent(returnPerTrade, 2)} · PF ${profitFactor.toFixed(2)}`
      },
      {
        label: '곡선 안정성',
        value: oosSharpeGap == null ? '-' : `${oosSharpeGap.toFixed(2)} gap`,
        tone: oosSharpeGap == null ? 'text-gray-300' : (oosSharpeGap <= 0.35 && (oosReturnGap ?? 0) <= 0.15 ? 'text-cyan-300' : 'text-red-300'),
        description: result.oosTotalReturn == null ? 'OOS 없음' : `IS/OOS 수익률 차이 ${formatPercent(oosReturnGap ?? 0, 2)}`
      }
    ]
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
    if (!pattern?.raw) {
      error = '최적화할 패턴을 선택하세요.'
      return
    }

    const symbols = parseSymbols(form.symbolsText)
    if (!symbols.length || !form.from || !form.to) {
      error = '종목과 기간을 입력하세요.'
      return
    }

    const entryPeriodValues = parseNumberList(form.entryPeriodValuesText)
    const exitPeriodValues = parseNumberList(form.exitPeriodValuesText)
    if (form.timingFocusMode && form.selectedEntryRuleIndex === '' && form.selectedExitRuleIndex === '') {
      error = '타이밍 최적화에서는 진입 규칙 또는 청산 규칙을 하나 이상 선택하세요.'
      return
    }
    if (form.timingFocusMode && form.selectedEntryRuleIndex !== '' && entryPeriodValues.length === 0) {
      error = '진입 기간 후보를 하나 이상 입력하세요.'
      return
    }
    if (form.timingFocusMode && form.selectedExitRuleIndex !== '' && exitPeriodValues.length === 0) {
      error = '청산 기간 후보를 하나 이상 입력하세요.'
      return
    }

    creating = true
    try {
      const optimizeParams = {
        atrStopMultiplier: form.includeRiskExitAxes ? buildRange(form.atrStopMin, form.atrStopMax, form.atrStopStep) : null,
        atrTargetMultiplier: form.includeRiskExitAxes ? buildRange(form.atrTargetMin, form.atrTargetMax, form.atrTargetStep) : null,
        maxHoldingBars: form.includeRiskExitAxes ? buildRange(form.maxHoldingMin, form.maxHoldingMax, form.maxHoldingStep) : null,
        trailingAtr: form.includeRiskExitAxes ? buildRange(form.trailingAtrMin, form.trailingAtrMax, form.trailingAtrStep) : null,
        partialProfitR: form.includeRiskExitAxes ? buildRange(form.partialProfitMin, form.partialProfitMax, form.partialProfitStep) : null,
        defaultAllocationPercent: form.includeRiskExitAxes ? buildRange(form.defaultAllocationMin, form.defaultAllocationMax, form.defaultAllocationStep) : null,
        ruleParamOverrides: [
          ...(form.timingFocusMode && form.selectedEntryRuleIndex !== '' ? [{
            scope: 'Entry',
            ruleIndex: toNumber(form.selectedEntryRuleIndex, 0),
            paramKey: 'period',
            values: entryPeriodValues
          }] : []),
          ...(form.timingFocusMode && form.selectedExitRuleIndex !== '' ? [{
            scope: 'Exit',
            ruleIndex: toNumber(form.selectedExitRuleIndex, 0),
            paramKey: 'period',
            values: exitPeriodValues
          }] : [])
        ],
        entryLogicOptions: form.timingFocusMode && !form.sweepEntryLogic ? null : form.entryLogicOptions,
        exitLogicOptions: form.timingFocusMode && !form.sweepExitLogic ? null : form.exitLogicOptions,
        requireBullRegimeOptions: form.timingFocusMode && !form.sweepRequireBullRegime ? null : form.requireBullRegimeOptions,
        entryModeOptions: form.timingFocusMode && !form.sweepEntryMode ? null : form.entryModeOptions,
        sizingModeOptions: form.timingFocusMode && !form.sweepSizingMode ? null : form.sizingModeOptions
      }

      await optimizationApi.create({
        name: form.jobName.trim() || `${pattern.name} 타이밍 최적화`,
        priority: toNumber(form.priority, 0),
        chunkSize: toNumber(form.chunkSize, 200),
        maxDurationHours: form.maxDurationHours === '' ? null : toNumber(form.maxDurationHours, 0),
        maxTestedCombinations: form.maxTestedCombinations === '' ? null : toNumber(form.maxTestedCombinations, 0),
        topResultsToKeep: toNumber(form.topResultsToKeep, 50),
        rankBy: form.rankBy,
        continuousMode: form.continuousMode,
        autoApplyBestResult: form.autoApplyBestResult,
        autoApplyMinTrades: toNumber(form.autoApplyMinTrades, 10),
        optimizeRequest: {
          basePattern: pattern.raw,
          symbols,
          from: form.from,
          to: form.to,
          initialCapital: 100000,
          dataSource: form.dataSource || null,
          timeFrame: form.timeFrame,
          rankBy: form.rankBy,
          maxResults: toNumber(form.maxResults, 10),
          maxCombinations: toNumber(form.maxCombinations, 500),
          oosPercent: toNumber(form.oosPercent, 0.25),
          optimizeParams
        }
      })
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
        <h2 class="text-3xl font-bold">최적화</h2>
        <p class="mt-2 text-sm text-gray-400">백테스트에서 구조를 고른 뒤, 여기서는 진입/청산 기간과 핵심 실행 옵션만 좁게 스윕해 로버스트한 조합을 찾습니다.</p>
      </div>
      <button on:click={loadJobs} class="flex items-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">
        <RotateCcw size={16} />
        {refreshing ? '새로고침 중...' : '새로고침'}
      </button>
    </div>

    {#if error}
      <div class="rounded-lg border border-red-700 bg-red-900/20 p-4 text-red-300">{error}</div>
    {/if}

    <section class="rounded-2xl border border-gray-800 bg-gray-950 p-6">
      <div class="mb-5 flex items-center gap-2">
        <Zap size={18} class="text-blue-400" />
        <h3 class="text-xl font-semibold">새 최적화 작업</h3>
      </div>

      <div class="grid grid-cols-1 gap-4 xl:grid-cols-4">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">패턴</div>
          <select bind:value={form.patternId} on:change={() => { const pattern = currentPattern(); form.selectedEntryRuleIndex = ''; form.selectedExitRuleIndex = ''; if (pattern) { form.jobName = `${pattern.name} 타이밍 최적화`; syncTimingDefaults(pattern); } }} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each patterns as pattern}
              <option value={pattern.id}>{pattern.name}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300 xl:col-span-2">
          <div class="mb-2 text-gray-500">종목</div>
          <input bind:value={form.symbolsText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="SPY, QQQ, TQQQ" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">작업 이름</div>
          <input bind:value={form.jobName} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="패턴 최적화" />
        </label>

        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">시작일</div>
          <input type="date" bind:value={form.from} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">종료일</div>
          <input type="date" bind:value={form.to} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">타임프레임</div>
          <select bind:value={form.timeFrame} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each timeFrameOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">데이터 소스</div>
          <select bind:value={form.dataSource} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each dataSourceOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
      </div>

      <div class="mt-6">
        <FinancialFactorBuilder bind:symbolsText={form.symbolsText} title="최적화용 재무 팩터 빌더" description="가치/흑자/턴어라운드/성장 조건으로 유니버스를 먼저 고른 뒤 그 집합에 대해서만 타이밍 최적화를 돌립니다." />
      </div>

      <div class="mt-6">
        <UniverseBuilder bind:symbolsText={form.symbolsText} title="최적화용 유니버스 빌더" description="백테스트에서 효과를 본 시총/섹터 유니버스를 그대로 가져와 타이밍 최적화의 입력 종목군으로 씁니다." />
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-5">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">정렬 기준</div>
          <select bind:value={form.rankBy} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white">
            {#each rankOptions as [value, label]}
              <option value={value}>{label}</option>
            {/each}
          </select>
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">표시 결과 수</div>
          <input type="number" bind:value={form.maxResults} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 조합 수</div>
          <input type="number" bind:value={form.maxCombinations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">OOS 비율</div>
          <input type="number" step="0.05" min="0" max="0.5" bind:value={form.oosPercent} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">우선순위</div>
          <input type="number" bind:value={form.priority} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-4">
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">Chunk 크기</div>
          <input type="number" bind:value={form.chunkSize} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 실행 시간(시간)</div>
          <input type="number" step="0.5" bind:value={form.maxDurationHours} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비우면 무제한" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">최대 테스트 수</div>
          <input type="number" bind:value={form.maxTestedCombinations} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="비우면 무제한" />
        </label>
        <label class="text-sm text-gray-300">
          <div class="mb-2 text-gray-500">보관 결과 수</div>
          <input type="number" bind:value={form.topResultsToKeep} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" />
        </label>
      </div>

      <div class="mt-6 rounded-xl border border-blue-700/40 bg-blue-950/20 p-5">
        <div class="flex items-center justify-between gap-4">
          <div>
            <div class="text-base font-semibold text-white">타이밍 전용 스윕</div>
            <div class="mt-1 text-sm text-blue-100">현재 패턴의 진입/청산 룰 기간만 먼저 좁게 최적화합니다. 구조 비교는 백테스트의 타이밍 연구실에서 끝낸 뒤 이 화면으로 넘어오는 흐름이 맞습니다.</div>
          </div>
          <label class="flex items-center gap-2 text-sm text-blue-100">
            <input type="checkbox" bind:checked={form.timingFocusMode} />
            타이밍 모드 사용
          </label>
        </div>

        <div class="mt-5 grid grid-cols-1 gap-4 xl:grid-cols-2">
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium text-white">진입 기간 스윕</div>
            <select bind:value={form.selectedEntryRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" disabled={!form.timingFocusMode}>
              <option value="">선택 안 함</option>
              {#each timingEntryRules() as rule}
                <option value={rule.index}>{rule.label}</option>
              {/each}
            </select>
            <label class="mt-3 block text-sm text-gray-300">
              <div class="mb-2 text-gray-500">진입 기간 후보</div>
              <input bind:value={form.entryPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" placeholder="10, 20, 30" disabled={!form.timingFocusMode || form.selectedEntryRuleIndex === ''} />
            </label>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium text-white">청산 기간 스윕</div>
            <select bind:value={form.selectedExitRuleIndex} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" disabled={!form.timingFocusMode}>
              <option value="">선택 안 함</option>
              {#each timingExitRules() as rule}
                <option value={rule.index}>{rule.label}</option>
              {/each}
            </select>
            <label class="mt-3 block text-sm text-gray-300">
              <div class="mb-2 text-gray-500">청산 기간 후보</div>
              <input bind:value={form.exitPeriodValuesText} class="w-full rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white disabled:opacity-40" placeholder="5, 10, 20" disabled={!form.timingFocusMode || form.selectedExitRuleIndex === ''} />
            </label>
          </div>
        </div>

        <div class="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-3">
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="mb-2 font-medium text-white">함께 스윕할 옵션</div>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepEntryLogic} disabled={!form.timingFocusMode} /> 진입 로직</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepExitLogic} disabled={!form.timingFocusMode} /> 청산 로직</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepRequireBullRegime} disabled={!form.timingFocusMode} /> 강세장 제한 on/off</label>
            <label class="mb-2 flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepEntryMode} disabled={!form.timingFocusMode} /> 진입 방식(CurrentClose / NextOpen)</label>
            <label class="flex items-center gap-2"><input type="checkbox" bind:checked={form.sweepSizingMode} disabled={!form.timingFocusMode} /> 사이징 방식(FixedRisk / Kelly / HalfKelly)</label>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4 text-sm text-gray-300">
            <div class="mb-2 font-medium text-white">예상 조합 수</div>
            <div class="text-3xl font-bold text-blue-300">{estimatedCombinationCount().toLocaleString('ko-KR')}</div>
            <div class="mt-2 text-xs text-gray-500">타이밍 축과 선택한 옵션을 기준으로 대략 계산한 값입니다.</div>
          </div>

          <div class="rounded-lg border border-amber-700 bg-amber-900/10 p-4 text-sm text-amber-100">
            <div class="mb-2 font-medium text-amber-200">권장 흐름</div>
            <div>1. 백테스트에서 구조를 정합니다.</div>
            <div>2. 여기서는 진입/청산 기간만 좁게 스윕합니다.</div>
            <div>3. 손절/보유/비중 축은 아래 보조 탐색을 켰을 때만 같이 돕니다.</div>
          </div>
        </div>
      </div>

      <div class="mt-6 rounded-xl border border-gray-800 bg-gray-900 p-5">
        <div class="mb-4 flex items-center justify-between gap-4">
          <div class="text-sm font-semibold text-white">보조 리스크 / 청산 축</div>
          <label class="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" bind:checked={form.includeRiskExitAxes} />
            함께 탐색
          </label>
        </div>
        <div class={`grid grid-cols-1 gap-4 xl:grid-cols-3 ${form.includeRiskExitAxes ? '' : 'opacity-50'}`}>
          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">손절 / 목표</div>
            <div class="grid grid-cols-3 gap-2 text-xs text-gray-300">
              <input type="number" step="0.1" bind:value={form.atrStopMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrStopMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrStopStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="손절 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.atrTargetStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="목표 간격" disabled={!form.includeRiskExitAxes} />
            </div>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">보유 / 청산</div>
            <div class="grid grid-cols-3 gap-2 text-xs text-gray-300">
              <input type="number" bind:value={form.maxHoldingMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" bind:value={form.maxHoldingMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" bind:value={form.maxHoldingStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="보유 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.trailingAtrStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="트레일 간격" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 최소" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 최대" disabled={!form.includeRiskExitAxes} />
              <input type="number" step="0.1" bind:value={form.partialProfitStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="부분익절 간격" disabled={!form.includeRiskExitAxes} />
            </div>
          </div>

          <div class="rounded-lg border border-gray-800 bg-gray-950 p-4">
            <div class="mb-3 font-medium">전략 옵션</div>
            <div class="space-y-3 text-sm text-gray-300">
              <label class="block">
                <div class="mb-2 text-gray-500">기본 비중 범위</div>
                <div class="grid grid-cols-3 gap-2">
                  <input type="number" bind:value={form.defaultAllocationMin} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="최소" disabled={!form.includeRiskExitAxes} />
                  <input type="number" bind:value={form.defaultAllocationMax} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="최대" disabled={!form.includeRiskExitAxes} />
                  <input type="number" bind:value={form.defaultAllocationStep} class="rounded border border-gray-700 bg-gray-900 px-2 py-2 text-white" placeholder="간격" disabled={!form.includeRiskExitAxes} />
                </div>
              </label>
              <div class="grid grid-cols-2 gap-2">
                <div class="rounded border border-gray-800 bg-gray-900 p-3">
                  <div class="mb-2 text-xs text-gray-500">진입 로직 후보</div>
                  {#each logicOptionValues as [value, label]}
                    <label class="mb-1 flex items-center gap-2 text-xs">
                      <input type="checkbox" checked={form.entryLogicOptions.includes(value)} on:change={(e) => form.entryLogicOptions = e.currentTarget.checked ? [...form.entryLogicOptions, value] : form.entryLogicOptions.filter((item) => item !== value)} />
                      {label}
                    </label>
                  {/each}
                </div>
                <div class="rounded border border-gray-800 bg-gray-900 p-3">
                  <div class="mb-2 text-xs text-gray-500">청산 로직 후보</div>
                  {#each logicOptionValues as [value, label]}
                    <label class="mb-1 flex items-center gap-2 text-xs">
                      <input type="checkbox" checked={form.exitLogicOptions.includes(value)} on:change={(e) => form.exitLogicOptions = e.currentTarget.checked ? [...form.exitLogicOptions, value] : form.exitLogicOptions.filter((item) => item !== value)} />
                      {label}
                    </label>
                  {/each}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 xl:grid-cols-3">
        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">장세 / 진입 방식</div>
          <div class="grid grid-cols-2 gap-3">
            <div>
              <div class="mb-2 text-xs text-gray-500">강세장 제한</div>
              {#each yesNoOptions as [value, label]}
                <label class="mb-1 flex items-center gap-2 text-xs">
                  <input type="checkbox" checked={form.requireBullRegimeOptions.includes(value)} on:change={(e) => form.requireBullRegimeOptions = e.currentTarget.checked ? [...form.requireBullRegimeOptions, value] : form.requireBullRegimeOptions.filter((item) => item !== value)} />
                  {label}
                </label>
              {/each}
            </div>
            <div>
              <div class="mb-2 text-xs text-gray-500">진입 방식</div>
              {#each [['CurrentClose', '현재 봉 종가'], ['NextOpen', '다음 봉 시가']] as [value, label]}
                <label class="mb-1 flex items-center gap-2 text-xs">
                  <input type="checkbox" checked={form.entryModeOptions.includes(value)} on:change={(e) => form.entryModeOptions = e.currentTarget.checked ? [...form.entryModeOptions, value] : form.entryModeOptions.filter((item) => item !== value)} />
                  {label}
                </label>
              {/each}
            </div>
          </div>
        </div>

        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">사이징 방식</div>
          {#each [['FixedRisk', '고정 리스크'], ['Kelly', '켈리'], ['HalfKelly', '하프 켈리']] as [value, label]}
            <label class="mb-2 flex items-center gap-2 text-xs">
              <input type="checkbox" checked={form.sizingModeOptions.includes(value)} on:change={(e) => form.sizingModeOptions = e.currentTarget.checked ? [...form.sizingModeOptions, value] : form.sizingModeOptions.filter((item) => item !== value)} />
              {label}
            </label>
          {/each}
        </div>

        <div class="rounded-lg border border-gray-800 bg-gray-900 p-4 text-sm text-gray-300">
          <div class="mb-3 font-medium text-white">자동 운용</div>
          <label class="mb-2 flex items-center gap-2">
            <input type="checkbox" bind:checked={form.continuousMode} />
            연속 최적화 모드
          </label>
          <label class="mb-3 flex items-center gap-2">
            <input type="checkbox" bind:checked={form.autoApplyBestResult} />
            완료 후 최고 결과 자동 반영
          </label>
          <label class="block">
            <div class="mb-2 text-xs text-gray-500">자동 반영 최소 거래 수</div>
            <input type="number" bind:value={form.autoApplyMinTrades} class="w-full rounded border border-gray-700 bg-gray-950 px-3 py-2 text-white" />
          </label>
        </div>
      </div>

      <div class="mt-6 flex justify-end">
        <button on:click={createJob} disabled={creating || loading} class="flex items-center gap-2 rounded bg-green-600 px-5 py-3 text-sm font-semibold text-white transition hover:bg-green-700 disabled:opacity-50">
          <Save size={16} />
          {creating ? '생성 중...' : '최적화 작업 생성'}
        </button>
      </div>
    </section>

    <section class="space-y-4">
      <div class="flex items-center justify-between">
        <h3 class="text-xl font-semibold">최적화 작업 목록</h3>
        <div class="text-sm text-gray-500">총 {jobs.length}개</div>
      </div>

      {#if loading}
        <div class="rounded-xl border border-gray-800 bg-gray-950 p-10 text-center text-gray-400">불러오는 중...</div>
      {:else if jobs.length === 0}
        <div class="rounded-xl border border-gray-800 bg-gray-950 p-10 text-center text-gray-400">아직 생성된 최적화 작업이 없습니다.</div>
      {:else}
        {#each jobs as job (job.id)}
          <div class="overflow-hidden rounded-2xl border border-gray-800 bg-gray-950">
            <div class="flex items-start justify-between gap-4 p-5">
              <button on:click={() => toggleExpand(job.id)} class="flex-1 text-left">
                <div class="mb-3 flex items-center gap-3">
                  <h4 class="text-lg font-semibold">{job.name}</h4>
                  <span class={`rounded px-3 py-1 text-xs ${statusClass(job.status)}`}>{job.status}</span>
                </div>
                <div class="mb-3">
                  <div class="mb-1 flex justify-between text-xs text-gray-400">
                    <span>{job.completedCombinations} / {job.totalCombinations} 조합</span>
                    <span>{Number(job.progress ?? 0).toFixed(1)}%</span>
                  </div>
                  <div class="h-2 rounded-full bg-gray-800">
                    <div class="h-2 rounded-full bg-blue-500 transition-all" style={`width:${Math.min(100, Number(job.progress ?? 0))}%`}></div>
                  </div>
                </div>
                <div class="text-xs text-gray-500">
                  생성 {formatDate(job.createdAt)}
                  {#if job.startedAt} · 시작 {formatDate(job.startedAt)}{/if}
                  {#if job.completedAt} · 완료 {formatDate(job.completedAt)}{/if}
                </div>
              </button>

              <div class="flex items-center gap-2">
                {#if job.status === 'Running'}
                  <button on:click={() => changeJobState(job, 'pause')} class="rounded p-2 text-yellow-300 transition hover:bg-yellow-950/30" title="일시정지">
                    <Pause size={16} />
                  </button>
                  <button on:click={() => changeJobState(job, 'cancel')} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="취소">
                    <Trash2 size={16} />
                  </button>
                {:else if job.status === 'Paused'}
                  <button on:click={() => changeJobState(job, 'resume')} class="rounded p-2 text-green-300 transition hover:bg-green-950/30" title="재개">
                    <Play size={16} />
                  </button>
                  <button on:click={() => changeJobState(job, 'cancel')} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="취소">
                    <Trash2 size={16} />
                  </button>
                {:else if ['Completed', 'Cancelled', 'Failed'].includes(job.status)}
                  <button on:click={() => removeJob(job)} class="rounded p-2 text-red-300 transition hover:bg-red-950/30" title="삭제">
                    <Trash2 size={16} />
                  </button>
                {/if}
                <ChevronDown size={18} class={`text-gray-500 transition ${expandedId === job.id ? 'rotate-180' : ''}`} />
              </div>
            </div>

            {#if expandedId === job.id}
              {@const detail = jobDetails[job.id]}
              {@const results = jobResults[job.id] ?? []}
              <div class="border-t border-gray-800 bg-gray-900/40 p-5">
                {#if !detail}
                  <div class="text-sm text-gray-400">상세 정보를 불러오는 중...</div>
                {:else}
                  <div class="grid grid-cols-1 gap-4 xl:grid-cols-4">
                    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
                      <div class="text-xs text-gray-500">경과 시간</div>
                      <div class="mt-2 text-lg font-semibold">{formatDuration(detail.elapsedSeconds)}</div>
                    </div>
                    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
                      <div class="text-xs text-gray-500">예상 남은 시간</div>
                      <div class="mt-2 text-lg font-semibold">{formatDuration(detail.estimatedRemainingSeconds)}</div>
                    </div>
                    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
                      <div class="text-xs text-gray-500">자동 반영 횟수</div>
                      <div class="mt-2 text-lg font-semibold">{detail.appliedResultCount ?? 0}</div>
                    </div>
                    <div class="rounded-xl border border-gray-800 bg-gray-950 p-4">
                      <div class="text-xs text-gray-500">마지막 진행 시간</div>
                      <div class="mt-2 text-sm font-semibold">{formatDate(detail.lastProgressAt)}</div>
                    </div>
                  </div>

                  {#if detail.errorMessage}
                    <div class="mt-4 rounded-lg border border-red-700 bg-red-900/20 p-4 text-sm text-red-300">{detail.errorMessage}</div>
                  {/if}

                  <div class="mt-4 rounded-xl border border-gray-800 bg-gray-950 p-4">
                    <div class="mb-3 text-sm font-semibold">자동 반영 설정</div>
                    <div class="grid grid-cols-1 gap-3 xl:grid-cols-[1fr,220px,140px]">
                      <label class="flex items-center gap-2 text-sm text-gray-300">
                        <input type="checkbox" bind:checked={detail.autoApplyBestResult} />
                        완료 후 최고 결과 자동 반영
                      </label>
                      <input type="number" bind:value={detail.autoApplyMinTrades} class="rounded border border-gray-700 bg-gray-900 px-3 py-2 text-white" placeholder="최소 거래 수" />
                      <button on:click={() => saveJobSettings(job.id)} class="rounded bg-blue-600 px-4 py-2 text-sm text-white transition hover:bg-blue-700">저장</button>
                    </div>
                    {#if detail.lastAutoApplyMessage}
                      <div class="mt-3 text-sm text-gray-400">{detail.lastAutoApplyMessage}</div>
                    {/if}
                  </div>

                  <div class="mt-5 rounded-xl border border-gray-800 bg-gray-950 p-4">
                    <div class="mb-4 text-sm font-semibold">상위 결과</div>
                    {#if results.length === 0}
                      <div class="text-sm text-gray-400">아직 결과가 없습니다.</div>
                    {:else}
                      <div class="space-y-3">
                        {#each results as result}
                          <div class="rounded-lg border border-gray-800 bg-gray-900 p-4">
                            <div class="flex flex-wrap items-start justify-between gap-4">
                              <div>
                                <div class="flex items-center gap-3">
                                  <div class="text-lg font-semibold">#{result.rank}</div>
                                  <div class="text-sm text-gray-400">{summaryParams(result)}</div>
                                </div>
                                <div class="mt-3 flex flex-wrap gap-4 text-sm">
                                  <span>수익률 <strong class="text-green-300">{formatPercent(result.totalReturn)}</strong></span>
                                  <span>샤프 <strong>{Number(result.sharpeRatio ?? 0).toFixed(2)}</strong></span>
                                  <span>소르티노 <strong>{Number(result.sortinoRatio ?? 0).toFixed(2)}</strong></span>
                                  <span>MDD <strong class="text-red-300">{formatPercent(result.maxDrawdown, 2)}</strong></span>
                                  <span>승률 <strong>{formatPercent(result.winRate)}</strong></span>
                                  <span>거래 수 <strong>{result.tradeCount}</strong></span>
                                </div>
                                {#if result.oosTotalReturn != null}
                                  <div class="mt-2 text-xs text-gray-400">
                                    OOS 수익률 {formatPercent(result.oosTotalReturn)} · OOS 샤프 {Number(result.oosSharpeRatio ?? 0).toFixed(2)} · OOS 거래 {result.oosTotalTrades ?? 0}
                                  </div>
                                {/if}
                                <div class="mt-4 grid grid-cols-1 gap-3 xl:grid-cols-4">
                                  {#each getResultInsights(result, results) as insight}
                                    <div class="rounded border border-gray-800 bg-gray-950 p-3">
                                      <div class="text-xs text-gray-500">{insight.label}</div>
                                      <div class={`mt-2 text-lg font-semibold ${insight.tone}`}>{insight.value}</div>
                                      <div class="mt-1 text-xs text-gray-400">{insight.description}</div>
                                    </div>
                                  {/each}
                                </div>
                              </div>
                              <button on:click={() => applyResult(job.id, result.id ?? null)} class="rounded bg-green-600 px-4 py-2 text-sm text-white transition hover:bg-green-700">
                                이 결과 반영
                              </button>
                            </div>
                          </div>
                        {/each}
                      </div>
                    {/if}
                  </div>
                {/if}
              </div>
            {/if}
          </div>
        {/each}
      {/if}
    </section>
  </div>
</div>

import { toStrategyDocument } from '../strategies/strategyDocument.js'

export function toNumber(value, fallback = 0) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

export function projectOptimizationRankingMetadata(metadata) {
  const rankings = metadata?.optimizationRankings ?? []
  const defaultRankBy = rankings.find((item) => item.isDefault)?.code
  if (!rankings.length || !defaultRankBy) {
    throw new Error('서버의 최적화 순위 메타데이터가 비어 있습니다.')
  }

  return {
    rankOptions: rankings.map((item) => [item.code, item.displayName]),
    defaultRankBy
  }
}

export function parseSymbols(text) {
  return String(text ?? '').split(',').map((item) => item.trim().toUpperCase()).filter(Boolean)
}

export function buildRange(min, max, step) {
  return { min: toNumber(min), max: toNumber(max), step: toNumber(step, 1) }
}

export function parseNumberList(text) {
  return [...new Set(String(text ?? '').split(',')
    .map((item) => Number(item.trim()))
    .filter((value) => Number.isFinite(value) && value > 0))]
    .sort((a, b) => a - b)
}

function safeParse(value, fallback) {
  try {
    return value ? JSON.parse(value) : fallback
  } catch {
    return fallback
  }
}

export function entryRules(rawPattern) {
  const groups = safeParse(rawPattern?.entryGroupsJson, [])
  return Array.isArray(groups) && groups.length > 0
    ? groups.flatMap((group) => group.rules ?? group.Rules ?? [])
    : safeParse(rawPattern?.entryRulesJson, [])
}

export function exitRules(rawPattern) {
  return safeParse(rawPattern?.exitRulesJson, [])
}

function ruleLabel(rule, index) {
  const indicator = rule?.indicator ?? rule?.Indicator ?? '규칙'
  const params = rule?.params ?? rule?.Params ?? {}
  const period = params.period != null ? ` · 기간 ${params.period}` : ''
  const refSymbol = (rule?.refSymbol ?? rule?.RefSymbol ?? '').trim()
  return `${index + 1}. ${refSymbol ? `${refSymbol} ` : ''}${indicator}${period}`
}

export function selectableRules(rules) {
  return rules.map((rule, index) => ({ index, label: ruleLabel(rule, index), rule }))
}

export function preferredRuleIndex(rules) {
  if (!rules.length) return ''
  const preferred = rules.findIndex((rule) => (rule?.params ?? rule?.Params ?? {}).period != null)
  return String(preferred >= 0 ? preferred : 0)
}

function rangeLength(range) {
  if (range.step <= 0) return 1
  return Math.max(1, Math.floor(((range.max - range.min) / range.step) + 1))
}

export function estimatedCombinationCount(form) {
  const focused = ['entry', 'exit', 'risk'].includes(form.tuningFocus)
  let total = 1
  if (form.timingFocusMode && (!focused || form.tuningFocus !== 'risk')) {
    if (form.selectedEntryRuleIndex !== '' && (!focused || form.tuningFocus === 'entry')) total *= Math.max(parseNumberList(form.entryPeriodValuesText).length, 1)
    if (form.selectedExitRuleIndex !== '' && (!focused || form.tuningFocus === 'exit')) total *= Math.max(parseNumberList(form.exitPeriodValuesText).length, 1)
    if (!focused && form.sweepEntryLogic) total *= Math.max(form.entryLogicOptions.length, 1)
    if (!focused && form.sweepExitLogic) total *= Math.max(form.exitLogicOptions.length, 1)
    if (!focused && form.sweepRequireBullRegime) total *= Math.max(form.requireBullRegimeOptions.length, 1)
    if (!focused && form.sweepEntryMode) total *= Math.max(form.entryModeOptions.length, 1)
    if (!focused && form.sweepSizingMode) total *= Math.max(form.sizingModeOptions.length, 1)
  }
  const useRiskRanges = focused ? form.tuningFocus === 'risk' : form.includeRiskExitAxes
  if (useRiskRanges) {
    const ranges = focused
      ? [buildRange(form.atrStopMin, form.atrStopMax, form.atrStopStep), buildRange(form.atrTargetMin, form.atrTargetMax, form.atrTargetStep)]
      : [
          buildRange(form.atrStopMin, form.atrStopMax, form.atrStopStep),
          buildRange(form.atrTargetMin, form.atrTargetMax, form.atrTargetStep),
          buildRange(form.maxHoldingMin, form.maxHoldingMax, form.maxHoldingStep),
          buildRange(form.trailingAtrMin, form.trailingAtrMax, form.trailingAtrStep),
          buildRange(form.partialProfitMin, form.partialProfitMax, form.partialProfitStep),
          buildRange(form.defaultAllocationMin, form.defaultAllocationMax, form.defaultAllocationStep)
        ]
    total *= ranges.map(rangeLength).reduce((product, length) => product * length, 1)
  }
  return total
}

export function buildOptimizationJob(form, pattern) {
  if (!pattern?.raw) return { error: '최적화할 패턴을 선택하세요.' }
  const symbols = parseSymbols(form.symbolsText)
  if (!symbols.length || !form.from || !form.to) return { error: '종목과 기간을 입력하세요.' }

  const entryPeriods = parseNumberList(form.entryPeriodValuesText)
  const exitPeriods = parseNumberList(form.exitPeriodValuesText)
  const focused = ['entry', 'exit', 'risk'].includes(form.tuningFocus)
  const useEntryPeriod = form.timingFocusMode && form.selectedEntryRuleIndex !== '' && (!focused || form.tuningFocus === 'entry')
  const useExitPeriod = form.timingFocusMode && form.selectedExitRuleIndex !== '' && (!focused || form.tuningFocus === 'exit')
  const useRiskRanges = focused ? form.tuningFocus === 'risk' : form.includeRiskExitAxes
  if (form.timingFocusMode && (!focused || form.tuningFocus !== 'risk') && !useEntryPeriod && !useExitPeriod)
    return { error: '타이밍 최적화에서는 진입 규칙 또는 청산 규칙을 하나 이상 선택하세요.' }
  if (useEntryPeriod && !entryPeriods.length)
    return { error: '진입 기간 후보를 하나 이상 입력하세요.' }
  if (useExitPeriod && !exitPeriods.length)
    return { error: '청산 기간 후보를 하나 이상 입력하세요.' }

  const optionalRange = (min, max, step) => useRiskRanges ? buildRange(min, max, step) : null
  const optimizeParams = {
    atrStopMultiplier: optionalRange(form.atrStopMin, form.atrStopMax, form.atrStopStep),
    atrTargetMultiplier: optionalRange(form.atrTargetMin, form.atrTargetMax, form.atrTargetStep),
    maxHoldingBars: focused ? null : optionalRange(form.maxHoldingMin, form.maxHoldingMax, form.maxHoldingStep),
    trailingAtr: focused ? null : optionalRange(form.trailingAtrMin, form.trailingAtrMax, form.trailingAtrStep),
    partialProfitR: focused ? null : optionalRange(form.partialProfitMin, form.partialProfitMax, form.partialProfitStep),
    defaultAllocationPercent: focused ? null : optionalRange(form.defaultAllocationMin, form.defaultAllocationMax, form.defaultAllocationStep),
    ruleParamOverrides: [
      ...(useEntryPeriod ? [{ scope: 'Entry', ruleIndex: toNumber(form.selectedEntryRuleIndex), paramKey: 'period', values: entryPeriods }] : []),
      ...(useExitPeriod ? [{ scope: 'Exit', ruleIndex: toNumber(form.selectedExitRuleIndex), paramKey: 'period', values: exitPeriods }] : [])
    ],
    entryLogicOptions: focused || (form.timingFocusMode && !form.sweepEntryLogic) ? null : form.entryLogicOptions,
    exitLogicOptions: focused || (form.timingFocusMode && !form.sweepExitLogic) ? null : form.exitLogicOptions,
    requireBullRegimeOptions: focused || (form.timingFocusMode && !form.sweepRequireBullRegime) ? null : form.requireBullRegimeOptions,
    entryModeOptions: focused || (form.timingFocusMode && !form.sweepEntryMode) ? null : form.entryModeOptions,
    sizingModeOptions: focused || (form.timingFocusMode && !form.sweepSizingMode) ? null : form.sizingModeOptions
  }
  return { payload: {
    name: form.jobName.trim() || `${pattern.name} 타이밍 최적화`,
    priority: toNumber(form.priority),
    chunkSize: toNumber(form.chunkSize, 200),
    maxDurationHours: form.maxDurationHours === '' ? null : toNumber(form.maxDurationHours),
    maxTestedCombinations: form.maxTestedCombinations === '' ? null : toNumber(form.maxTestedCombinations),
    topResultsToKeep: toNumber(form.topResultsToKeep, 50),
    rankBy: form.rankBy,
    continuousMode: form.continuousMode,
    autoApplyBestResult: form.autoApplyBestResult,
    autoApplyMinTrades: toNumber(form.autoApplyMinTrades, 10),
    optimizeRequest: {
      basePattern: toStrategyDocument(pattern.raw), symbols, from: form.from, to: form.to,
      initialCapital: 100000, dataSource: form.dataSource || null,
      timeFrame: form.timeFrame, rankBy: form.rankBy,
      maxResults: toNumber(form.maxResults, 10),
      maxCombinations: toNumber(form.maxCombinations, 500),
      oosPercent: toNumber(form.oosPercent, 0.25), optimizeParams
    }
  } }
}

export function formatDate(value) {
  return value ? new Date(value).toLocaleString('ko-KR') : '-'
}

export function formatPercent(value, digits = 1) {
  return `${(Number(value ?? 0) * 100).toFixed(digits)}%`
}

export function formatSignedPercent(value, digits = 1) {
  const number = Number(value ?? 0)
  return `${number > 0 ? '+' : ''}${(number * 100).toFixed(digits)}%`
}

export function formatDuration(seconds) {
  if (seconds == null || !Number.isFinite(seconds)) return '-'
  const total = Math.max(0, Math.round(seconds))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const remainder = total % 60
  if (hours > 0) return `${hours}시간 ${minutes}분`
  if (minutes > 0) return `${minutes}분 ${remainder}초`
  return `${remainder}초`
}

export function statusClass(status) {
  return ({ Pending: 'bg-yellow-950/60 text-yellow-300', Running: 'bg-blue-950/60 text-blue-300', Paused: 'bg-purple-950/60 text-purple-300', Completed: 'bg-green-950/60 text-green-300', Failed: 'bg-red-950/60 text-red-300', Cancelled: 'bg-gray-800 text-gray-300' })[status] ?? 'bg-gray-800 text-gray-300'
}

export function summaryParams(result, entryLabels = {}, sizingLabels = {}) {
  const params = result?.params ?? {}
  const overrides = params.ruleOverrides ?? params.RuleOverrides ?? []
  const period = (scope) => overrides.find((item) => (item.scope ?? item.Scope ?? 'Entry') === scope && (item.paramKey ?? item.ParamKey) === 'period')?.value
  return [period('Entry') != null ? `진입기간 ${period('Entry')}` : '', period('Exit') != null ? `청산기간 ${period('Exit')}` : '', params.atrStopMultiplier != null ? `손절 ${params.atrStopMultiplier}` : '', params.atrTargetMultiplier != null ? `목표 ${params.atrTargetMultiplier}` : '', params.maxHoldingBars != null ? `보유 ${params.maxHoldingBars}봉` : '', params.defaultAllocationPercent != null ? `기본비중 ${params.defaultAllocationPercent}%` : '', params.entryLogic ? `진입 ${params.entryLogic}` : '', params.exitLogic ? `청산 ${params.exitLogic}` : '', params.entryMode ? `진입방식 ${entryLabels[params.entryMode] ?? params.entryMode}` : '', params.sizingMode ? `사이징 ${sizingLabels[params.sizingMode] ?? params.sizingMode}` : ''].filter(Boolean).join(' · ')
}

function median(values) {
  const sorted = values.filter(Number.isFinite).sort((a, b) => a - b)
  if (!sorted.length) return null
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

export function resultInsights(result, results) {
  const benchmark = {
    trades: median(results.map((item) => Number(item.tradeCount ?? 0))),
    drawdown: median(results.map((item) => Number(item.maxDrawdown ?? 0))),
    profitFactor: median(results.map((item) => Number(item.profitFactor ?? 0))),
    returnPerTrade: median(results.map((item) => Number(item.totalReturn ?? 0) / Math.max(1, Number(item.tradeCount ?? 0))))
  }
  const trades = Number(result.tradeCount ?? 0)
  const drawdown = Number(result.maxDrawdown ?? 0)
  const profitFactor = Number(result.profitFactor ?? 0)
  const returnPerTrade = Number(result.totalReturn ?? 0) / Math.max(1, trades)
  const sharpeGap = result.oosSharpeRatio == null ? null : Math.abs(Number(result.sharpeRatio ?? 0) - Number(result.oosSharpeRatio ?? 0))
  const returnGap = result.oosTotalReturn == null ? null : Math.abs(Number(result.totalReturn ?? 0) - Number(result.oosTotalReturn ?? 0))
  return [
    { label: '낙폭 절감', value: benchmark.drawdown == null ? formatPercent(drawdown, 2) : formatSignedPercent(benchmark.drawdown - drawdown, 2), tone: benchmark.drawdown == null || drawdown <= benchmark.drawdown ? 'text-green-300' : 'text-red-300', description: benchmark.drawdown == null ? '현재 낙폭' : '중앙값 대비 개선' },
    { label: '거래 수 절감', value: benchmark.trades == null ? `${trades}` : `${benchmark.trades - trades > 0 ? '+' : ''}${Math.round(benchmark.trades - trades)}`, tone: benchmark.trades == null || trades <= benchmark.trades ? 'text-blue-200' : 'text-red-300', description: benchmark.trades == null ? '현재 거래 수' : '중앙값 대비 감소' },
    { label: '휩소 억제 추정', value: benchmark.returnPerTrade == null ? formatPercent(returnPerTrade, 2) : formatSignedPercent(returnPerTrade - benchmark.returnPerTrade, 2), tone: (benchmark.trades == null || trades <= benchmark.trades) && (benchmark.profitFactor == null || profitFactor >= benchmark.profitFactor) ? 'text-emerald-300' : 'text-red-300', description: `거래당 수익 ${formatPercent(returnPerTrade, 2)} · PF ${profitFactor.toFixed(2)}` },
    { label: '곡선 안정성', value: sharpeGap == null ? '-' : `${sharpeGap.toFixed(2)} gap`, tone: sharpeGap == null ? 'text-gray-300' : (sharpeGap <= 0.35 && (returnGap ?? 0) <= 0.15 ? 'text-cyan-300' : 'text-red-300'), description: result.oosTotalReturn == null ? 'OOS 없음' : `IS/OOS 수익률 차이 ${formatPercent(returnGap ?? 0, 2)}` }
  ]
}

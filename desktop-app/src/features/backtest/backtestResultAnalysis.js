import {
  factorDrawdownImprovement,
  factorReturnLift,
  formatDecimal,
  formatPercent,
  formatSignedPercent,
  getEquityCurveVolatility,
  getWhipsawStats
} from './backtestResearch.js'
import { factorScenarioScore } from './backtestScenarioPlanning.js'

export function findBaselineEntry(results, groupKey = 'current') {
  return results.find((item) => item.comparisonGroupKey === groupKey && item.isBaseline)
    ?? results.find((item) => item.isBaseline)
    ?? null
}

export function calculateComparisonDelta(results, entry) {
  const baseline = findBaselineEntry(results, entry?.comparisonGroupKey ?? 'current')?.data
  if (!baseline || !entry || entry.isBaseline) return null
  const baselineWhipsaw = getWhipsawStats(baseline)
  const currentWhipsaw = getWhipsawStats(entry.data)
  const baselineVolatility = getEquityCurveVolatility(baseline)
  const currentVolatility = getEquityCurveVolatility(entry.data)
  return {
    returnDelta: Number(entry.data.totalReturn ?? 0) - Number(baseline.totalReturn ?? 0),
    drawdownImprovement: Number(baseline.maxDrawdown ?? 0) - Number(entry.data.maxDrawdown ?? 0),
    tradeReduction: Number(baseline.totalTrades ?? 0) - Number(entry.data.totalTrades ?? 0),
    whipsawReduction: baselineWhipsaw.count - currentWhipsaw.count,
    whipsawRateImprovement: baselineWhipsaw.rate - currentWhipsaw.rate,
    stabilityImprovement: baselineVolatility != null && currentVolatility != null ? baselineVolatility - currentVolatility : null
  }
}

export function buildTimingReport(results, entry) {
  if (!entry || entry.isBaseline || !findBaselineEntry(results, entry.comparisonGroupKey ?? 'current')) return null
  const delta = calculateComparisonDelta(results, entry)
  const currentWhipsaw = getWhipsawStats(entry.data)
  return {
    drawdownImprovement: delta?.drawdownImprovement ?? 0,
    tradeReduction: delta?.tradeReduction ?? 0,
    whipsawReduction: delta?.whipsawReduction ?? 0,
    whipsawRateImprovement: delta?.whipsawRateImprovement ?? 0,
    stabilityImprovement: delta?.stabilityImprovement,
    currentWhipsawRate: currentWhipsaw.rate,
    currentWhipsawCount: currentWhipsaw.count,
    currentVolatility: getEquityCurveVolatility(entry.data)
  }
}

export function buildUniverseComparisonRows(results) {
  const baseEntry = findBaselineEntry(results, 'current')
  const baseSymbols = baseEntry?.data?.request?.symbols?.length ?? 0
  const rows = []
  const seen = new Set()
  for (const entry of results) {
    if (seen.has(entry.comparisonGroupKey)) continue
    const baselineEntry = findBaselineEntry(results, entry.comparisonGroupKey)
    if (!baselineEntry) continue
    seen.add(entry.comparisonGroupKey)
    rows.push({ key: entry.comparisonGroupKey, label: entry.comparisonGroupLabel, symbolCount: baselineEntry.data.request.symbols.length, symbolReduction: baseSymbols ? baseSymbols - baselineEntry.data.request.symbols.length : 0, totalReturn: baselineEntry.data.totalReturn, maxDrawdown: baselineEntry.data.maxDrawdown, sharpeRatio: baselineEntry.data.sharpeRatio, totalTrades: baselineEntry.data.totalTrades })
  }
  return rows
}

function compareScenarioPerformance(left, right, rankingMode) {
  const scoreDiff = factorScenarioScore(right, rankingMode) - factorScenarioScore(left, rankingMode)
  if (Math.abs(scoreDiff) > 0.000001) return scoreDiff
  const returnDiff = Number(right?.data?.totalReturn ?? 0) - Number(left?.data?.totalReturn ?? 0)
  if (Math.abs(returnDiff) > 0.000001) return returnDiff
  return Number(right?.data?.sharpeRatio ?? 0) - Number(left?.data?.sharpeRatio ?? 0)
}

export function buildFactorLabRankingRows(results, summaries, rankingMode, limit = 5) {
  const grouped = new Map()
  for (const entry of results.filter((item) => item.comparisonGroupKind === 'factor-lab')) {
    const current = grouped.get(entry.comparisonGroupKey) ?? []
    current.push(entry)
    grouped.set(entry.comparisonGroupKey, current)
  }
  const rows = [...grouped.entries()].map(([groupKey, entries]) => {
    const baselineEntry = entries.find((item) => item.isBaseline) ?? entries[0]
    const bestEntry = [...entries].sort((left, right) => compareScenarioPerformance(left, right, rankingMode))[0] ?? baselineEntry
    const summary = summaries.find((item) => item.id === baselineEntry.factorPresetId)
    return { key: groupKey, label: baselineEntry.factorPresetLabel ?? baselineEntry.comparisonGroupLabel, note: summary?.note ?? baselineEntry.factorPresetNote ?? '', summaryTags: summary?.summaryTags ?? [], source: summary?.source ?? 'preset', symbolCount: baselineEntry.symbolCount, baselineReturn: baselineEntry.data.totalReturn, baselineDrawdown: baselineEntry.data.maxDrawdown, bestScenarioLabel: bestEntry.data?.timingScenario?.label ?? bestEntry.label, bestReturn: bestEntry.data.totalReturn, bestDrawdown: bestEntry.data.maxDrawdown, bestSharpe: bestEntry.data.sharpeRatio, bestTrades: bestEntry.data.totalTrades, bestScore: factorScenarioScore(bestEntry, rankingMode) }
  }).sort((left, right) => {
    const scoreDiff = Number(right.bestScore ?? 0) - Number(left.bestScore ?? 0)
    return Math.abs(scoreDiff) > 0.000001 ? scoreDiff : Number(right.bestReturn ?? 0) - Number(left.bestReturn ?? 0)
  })
  return rows.slice(0, Math.max(1, Number(limit ?? 5))).map((row, index) => ({ ...row, rank: index + 1 }))
}

export function buildFactorLabInsightCards(rows) {
  if (!rows.length) return []
  const winner = rows[0]
  const biggestLift = [...rows].sort((left, right) => factorReturnLift(right) - factorReturnLift(left))[0] ?? winner
  const strongestDefense = [...rows].sort((left, right) => factorDrawdownImprovement(right) - factorDrawdownImprovement(left))[0] ?? winner
  const highestSharpe = [...rows].sort((left, right) => Number(right.bestSharpe ?? 0) - Number(left.bestSharpe ?? 0))[0] ?? winner
  return [
    { key: 'winner', label: '현재 우승 조합', headline: winner.label, detail: `${winner.bestScenarioLabel} · 점수 ${formatDecimal(winner.bestScore)}`, accent: 'text-fuchsia-200' },
    { key: 'lift', label: '기준선 대비 최대 수익 개선', headline: formatSignedPercent(factorReturnLift(biggestLift)), detail: `${biggestLift.label} · ${biggestLift.bestScenarioLabel}`, accent: factorReturnLift(biggestLift) >= 0 ? 'text-emerald-300' : 'text-red-300' },
    { key: 'defense', label: '가장 방어적인 조합', headline: formatSignedPercent(factorDrawdownImprovement(strongestDefense)), detail: `${strongestDefense.label} · 낙폭 ${formatPercent(strongestDefense.bestDrawdown)}`, accent: factorDrawdownImprovement(strongestDefense) >= 0 ? 'text-cyan-300' : 'text-red-300' },
    { key: 'sharpe', label: '최고 샤프 조합', headline: formatDecimal(highestSharpe.bestSharpe), detail: `${highestSharpe.label} · ${highestSharpe.bestScenarioLabel}`, accent: 'text-blue-300' }
  ]
}

export function buildFactorLabSummaryLine(rows) {
  if (!rows.length) return ''
  const averageSymbols = rows.reduce((sum, row) => sum + Number(row.symbolCount ?? 0), 0) / rows.length
  const positiveLiftCount = rows.filter((row) => factorReturnLift(row) > 0).length
  return `상위 ${rows.length}개 조합 중 ${positiveLiftCount}개가 기준선 대비 수익률을 개선했고, 평균 종목 수는 ${averageSymbols.toFixed(1)}개입니다.`
}

export function buildScenarioComparisonRows(results) {
  return results.map((entry) => ({ ...entry, delta: calculateComparisonDelta(results, entry) }))
}

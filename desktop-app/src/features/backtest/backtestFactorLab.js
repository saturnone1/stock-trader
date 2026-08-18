import { buildFactorSummaryTags } from './backtestScenarioPlanning.js'
import { uniqueSymbols } from './backtestResearch.js'

export async function queryFactorLabCandidates({ definitions, baseSymbols, minMatchedSymbols, query }) {
  const normalizedBaseSymbols = uniqueSymbols(baseSymbols)
  const minimum = Number(minMatchedSymbols)
  const responses = await Promise.all(definitions.map(async (definition) => {
    const response = await query({
      ...definition.params,
      symbols: normalizedBaseSymbols.join(','),
      limit: Math.max(normalizedBaseSymbols.length, 20)
    })
    const matchedSymbols = uniqueSymbols((response?.data?.items ?? []).map((item) => item.symbol))
    const filteredSummary = response?.data?.comparison?.filtered ?? {
      count: 0,
      positiveEarningsCount: 0,
      turnaroundCount: 0
    }
    return {
      definition,
      matchedSymbols,
      matched: Number(response?.data?.matched ?? matchedSymbols.length),
      filteredSummary,
      summaryTags: buildFactorSummaryTags(filteredSummary)
    }
  }))

  return {
    summaries: responses.map((item) => ({
      id: item.definition.id,
      label: item.definition.label,
      note: item.definition.note,
      source: item.definition.source,
      matched: item.matched,
      eligible: item.matched >= minimum,
      filteredSummary: item.filteredSummary,
      summaryTags: item.summaryTags
    })),
    variants: responses
      .filter((item) => item.matchedSymbols.length >= minimum)
      .map((item) => ({
        key: `factorlab-${item.definition.id}`,
        kind: 'factor-lab',
        label: `팩터 실험 · ${item.definition.label}`,
        description: `${normalizedBaseSymbols.length}개 중 ${item.matchedSymbols.length}개가 ${item.definition.note} 조건을 만족합니다.`,
        symbols: item.matchedSymbols,
        symbolCount: item.matchedSymbols.length,
        factorPresetId: item.definition.id,
        factorPresetLabel: item.definition.label,
        factorPresetNote: item.definition.note
      }))
  }
}

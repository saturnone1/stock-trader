export const timingWindowOptions = [
  { id: '20-20', label: '20 / 20', entryPeriod: 20, exitPeriod: 20 },
  { id: '20-10', label: '20 / 10', entryPeriod: 20, exitPeriod: 10 },
  { id: '20-5', label: '20 / 5', entryPeriod: 20, exitPeriod: 5 },
  { id: '30-10', label: '30 / 10', entryPeriod: 30, exitPeriod: 10 }
]

export const timingStructureOptions = [
  { id: 'market', label: '시장 타이밍' },
  { id: 'market-stock', label: '시장 + 종목 타이밍' }
]

export const factorExperimentPresets = [
  { id: 'value-pe', label: '저PER 흑자', note: 'PER 15 이하 + 흑자', params: { peRatioMax: '15', positiveEarningsOnly: true, sortBy: 'peAsc' } },
  { id: 'value-pb', label: '저PBR 자산주', note: 'PBR 1.5 이하 + 흑자', params: { pbRatioMax: '1.5', positiveEarningsOnly: true, sortBy: 'pbAsc' } },
  { id: 'quality-roe', label: '퀄리티 ROE', note: 'ROE 12% 이상 + 영업이익률 8% 이상', params: { roePercentMin: '12', operatingMarginMin: '8', positiveEarningsOnly: true, sortBy: 'roeDesc' } },
  { id: 'revenue-growth', label: '매출 성장', note: '매출 성장 10% 이상', params: { revenueGrowthMin: '0.1', positiveEarningsOnly: true, sortBy: 'revenueGrowthDesc' } },
  { id: 'earnings-growth', label: '이익 성장', note: '순이익 성장 15% 이상', params: { netIncomeGrowthMin: '0.15', positiveEarningsOnly: true, sortBy: 'netIncomeGrowthDesc' } },
  { id: 'turnaround-growth', label: '턴어라운드 성장', note: '턴어라운드 + 매출/순이익 성장', params: { turnaroundOnly: true, revenueGrowthMin: '0.1', netIncomeGrowthMin: '0.1', positiveEarningsOnly: true, sortBy: 'netIncomeGrowthDesc' } }
]

export const factorRankingOptions = [
  { id: 'best-return', label: '수익률 우선' },
  { id: 'best-sharpe', label: '샤프 우선' },
  { id: 'balanced', label: '균형 점수' },
  { id: 'defensive', label: '낙폭 방어형' }
]

export function uniqueSymbols(symbols) {
  return [...new Set(symbols.map((item) => item.trim().toUpperCase()).filter(Boolean))]
}

export function intersectSymbols(baseSymbols, filterSymbols) {
  const allowed = new Set(uniqueSymbols(filterSymbols))
  return uniqueSymbols(baseSymbols).filter((symbol) => allowed.has(symbol))
}

export function formatPercent(value, digits = 2) {
  return `${(Number(value ?? 0) * 100).toFixed(digits)}%`
}

export function formatSignedPercent(value, digits = 2) {
  const numeric = Number(value ?? 0)
  return `${numeric > 0 ? '+' : ''}${(numeric * 100).toFixed(digits)}%`
}

export function formatMoney(value) {
  return new Intl.NumberFormat('ko-KR', { maximumFractionDigits: 0 }).format(Number(value ?? 0))
}

export function formatDate(dateStr) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleDateString('ko-KR')
}

export function formatDecimal(value, digits = 2) {
  if (value == null || Number.isNaN(Number(value))) return '-'
  return Number(value).toFixed(digits)
}

export function factorSourceLabel(source) {
  if (source === 'current-builder') return '현재 빌더'
  if (source === 'custom') return '커스텀'
  return '기본 프리셋'
}

export function factorReturnLift(row) {
  return Number(row?.bestReturn ?? 0) - Number(row?.baselineReturn ?? 0)
}

export function factorDrawdownImprovement(row) {
  return Number(row?.baselineDrawdown ?? 0) - Number(row?.bestDrawdown ?? 0)
}

export function timeframeBarMinutes(timeframe) {
  if (timeframe === 'OneMinute') return 1
  if (timeframe === 'FiveMinute') return 5
  if (timeframe === 'FifteenMinute') return 15
  if (timeframe === 'Weekly') return 5 * 390
  return 390
}

export function whipsawThresholdBars(timeframe) {
  if (timeframe === 'OneMinute') return 30
  if (timeframe === 'FiveMinute') return 12
  if (timeframe === 'FifteenMinute') return 8
  if (timeframe === 'Weekly') return 2
  return 3
}

export function estimateHoldingBars(trade, timeframe) {
  const start = new Date(trade.entryTime ?? trade.EntryTime ?? '').getTime()
  const end = new Date(trade.exitTime ?? trade.ExitTime ?? '').getTime()
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) return 1
  return Math.max(1, Math.round(((end - start) / 60000) / timeframeBarMinutes(timeframe)))
}

export function getWhipsawStats(sourceResult) {
  const trades = sourceResult?.trades ?? []
  if (!trades.length) return { count: 0, rate: 0, thresholdBars: whipsawThresholdBars(sourceResult?.usedTimeFrame) }
  const thresholdBars = whipsawThresholdBars(sourceResult?.usedTimeFrame)
  const count = trades.filter((trade) =>
    Number(trade.pnlPercent ?? trade.PnLPercent ?? 0) < 0
    && estimateHoldingBars(trade, sourceResult?.usedTimeFrame) <= thresholdBars).length
  return { count, rate: count / trades.length, thresholdBars }
}

export function getEquityCurveVolatility(sourceResult) {
  const curve = sourceResult?.equityCurve ?? []
  if (curve.length < 3) return null
  const returns = []
  for (let index = 1; index < curve.length; index += 1) {
    const previous = Number(curve[index - 1]?.equity ?? curve[index - 1]?.Equity ?? 0)
    const current = Number(curve[index]?.equity ?? curve[index]?.Equity ?? 0)
    if (previous > 0 && Number.isFinite(current)) returns.push((current - previous) / previous)
  }
  if (returns.length < 2) return null
  const mean = returns.reduce((sum, value) => sum + value, 0) / returns.length
  const variance = returns.reduce((sum, value) => sum + ((value - mean) ** 2), 0) / returns.length
  return Math.sqrt(variance)
}

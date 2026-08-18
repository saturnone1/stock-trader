export function formatPercentPoints(value, digits = 1) {
  return `${Number(value ?? 0).toFixed(digits)}%`
}

export function formatFractionPercent(value, digits = 1) {
  return `${(Number(value ?? 0) * 100).toFixed(digits)}%`
}

export function gradeColor(grade) {
  if (grade === 'StrongBuy' || grade === 'Buy') return 'text-green-300'
  if (grade === 'StrongSell' || grade === 'Sell') return 'text-red-300'
  return 'text-yellow-300'
}

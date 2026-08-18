import test from 'node:test'
import assert from 'node:assert/strict'
import {
  formatFractionPercent,
  formatPercentPoints,
  gradeColor,
} from './stockAnalysisModel.js'

test('formats percentage points without rescaling', () => {
  assert.equal(formatPercentPoints(62.345, 2), '62.34%')
})

test('formats fractional rates as percentages', () => {
  assert.equal(formatFractionPercent(0.625, 1), '62.5%')
})

test('maps recommendation grades to stable semantic colors', () => {
  assert.equal(gradeColor('Buy'), 'text-green-300')
  assert.equal(gradeColor('Sell'), 'text-red-300')
  assert.equal(gradeColor('Neutral'), 'text-yellow-300')
})

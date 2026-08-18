import test from 'node:test'
import assert from 'node:assert/strict'
import { queryFactorLabCandidates } from './backtestFactorLab.js'

test('factor candidate queries preserve API contract and build eligible universe variants', async () => {
  const calls = []
  const definitions = [{
    id: 'quality', label: '퀄리티', note: 'ROE 조건', source: 'preset',
    params: { roePercentMin: '12', positiveEarningsOnly: true }
  }]
  const result = await queryFactorLabCandidates({
    definitions,
    baseSymbols: ['aapl', 'MSFT', 'AAPL'],
    minMatchedSymbols: 2,
    query: async (payload) => {
      calls.push(payload)
      return {
        data: {
          matched: 2,
          items: [{ symbol: 'aapl' }, { symbol: 'MSFT' }, { symbol: 'AAPL' }],
          comparison: { filtered: { count: 2, positiveEarningsCount: 2, turnaroundCount: 0 } }
        }
      }
    }
  })

  assert.deepEqual(calls, [{
    roePercentMin: '12', positiveEarningsOnly: true, symbols: 'AAPL,MSFT', limit: 20
  }])
  assert.equal(result.summaries[0].eligible, true)
  assert.deepEqual(result.variants[0].symbols, ['AAPL', 'MSFT'])
  assert.equal(result.variants[0].description, '2개 중 2개가 ROE 조건 조건을 만족합니다.')
  assert.deepEqual(definitions[0].params, { roePercentMin: '12', positiveEarningsOnly: true })
})

test('factor candidate summaries remain visible when a result is below the execution minimum', async () => {
  const result = await queryFactorLabCandidates({
    definitions: [{ id: 'value', label: '가치', note: '저PER', source: 'custom', params: {} }],
    baseSymbols: ['SPY', 'QQQ'],
    minMatchedSymbols: 2,
    query: async () => ({ data: { matched: 1, items: [{ symbol: 'SPY' }] } })
  })

  assert.equal(result.summaries[0].eligible, false)
  assert.deepEqual(result.variants, [])
})

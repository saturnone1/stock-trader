import test from 'node:test'
import assert from 'node:assert/strict'
import { toStrategyDocument } from './strategyDocument.js'

test('stored strategy responses become storage-independent execution documents', () => {
  const raw = {
    id: 42,
    name: '추세 전략',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-02T00:00:00Z',
    entryRulesJson: '[]'
  }

  assert.deepEqual(toStrategyDocument(raw), {
    storedStrategyId: 42,
    name: '추세 전략',
    entryRulesJson: '[]'
  })
  assert.equal(raw.storedStrategyId, undefined)
})

test('inline documents remain inline and preserve an existing stored reference', () => {
  assert.deepEqual(toStrategyDocument({ name: '임시 전략' }), { name: '임시 전략' })
  assert.deepEqual(
    toStrategyDocument({ storedStrategyId: 7, name: '연속 최적화' }),
    { storedStrategyId: 7, name: '연속 최적화' }
  )
})

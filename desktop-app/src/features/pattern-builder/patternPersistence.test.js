import test from 'node:test'
import assert from 'node:assert/strict'
import { createPatternPersistence, patternPersistenceError } from './patternPersistence.js'

function fixture() {
  const calls = []
  const responses = {
    list: { data: [{ id: 1, name: '기존 전략' }] },
    create: { data: { id: 2, name: '새 전략', raw: { id: 2 } } },
    get: { data: { id: 1, name: '기존 전략', raw: { id: 1 } } },
    update: { data: { id: 1, name: '수정 전략', raw: { id: 1, saved: true } } }
  }
  const api = {
    async list() { calls.push(['list']); return responses.list },
    async create(payload) { calls.push(['create', payload]); return responses.create },
    async get(id) { calls.push(['get', id]); return responses.get },
    async update(id, payload) { calls.push(['update', id, payload]); return responses.update },
    async delete(id) { calls.push(['delete', id]) }
  }
  const persistence = createPatternPersistence({
    api,
    buildWorkspace: (raw) => ({ hydratedFrom: raw }),
    buildPatternPayload: (workspace) => ({ serializedName: workspace.name })
  })
  return { calls, responses, persistence }
}

test('pattern CRUD preserves API payload and workspace hydration contracts', async () => {
  const { calls, persistence } = fixture()

  assert.deepEqual(await persistence.list(), [{ id: 1, name: '기존 전략' }])
  assert.deepEqual(await persistence.create('  새 전략  '), {
    pattern: { id: 2, name: '새 전략', raw: { id: 2 } },
    workspace: { hydratedFrom: { id: 2 } }
  })
  assert.deepEqual(await persistence.open(1), {
    pattern: { id: 1, name: '기존 전략', raw: { id: 1 } },
    workspace: { hydratedFrom: { id: 1 } }
  })
  assert.deepEqual(await persistence.save(1, { name: '수정 전략' }), {
    pattern: { id: 1, name: '수정 전략', raw: { id: 1, saved: true } },
    workspace: { hydratedFrom: { id: 1, saved: true } }
  })
  await persistence.remove(1)

  assert.deepEqual(calls, [
    ['list'],
    ['create', { name: '새 전략', description: '' }],
    ['get', 1],
    ['update', 1, { serializedName: '수정 전략' }],
    ['delete', 1]
  ])
})

test('malformed responses fail closed and API errors retain server detail', async () => {
  const { responses, persistence } = fixture()
  responses.list = { data: null }
  responses.get = { data: null }

  assert.deepEqual(await persistence.list(), [])
  await assert.rejects(() => persistence.open(1), /서버가 매매 전략 정보를 반환하지 않았습니다/)
  assert.equal(
    patternPersistenceError({ response: { data: { error: '이름이 중복됩니다.' } } }, '저장 실패'),
    '이름이 중복됩니다.'
  )
  assert.equal(patternPersistenceError(new Error('network down'), '저장 실패'), 'network down')
  assert.equal(patternPersistenceError(null, '저장 실패'), '저장 실패')
})

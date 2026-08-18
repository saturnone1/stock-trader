import assert from 'node:assert/strict'
import test from 'node:test'

import { tradeApiError } from './tradeActivityModel.js'

test('trade validation outcomes retain every application error', () => {
  const error = { response: { data: { errors: ['잘못된 기간', '잘못된 페이지'] } } }

  assert.equal(tradeApiError(error, 'fallback'), '잘못된 기간 잘못된 페이지')
})

test('framework binding errors are flattened instead of hidden', () => {
  const error = {
    response: {
      data: { errors: { pattern: ['알 수 없는 패턴입니다.'], from: ['날짜 형식이 올바르지 않습니다.'] } }
    }
  }

  assert.equal(
    tradeApiError(error, 'fallback'),
    '알 수 없는 패턴입니다. 날짜 형식이 올바르지 않습니다.')
})

test('network errors retain their message and then use the caller fallback', () => {
  assert.equal(tradeApiError(new Error('network down'), 'fallback'), 'network down')
  assert.equal(tradeApiError({}, 'fallback'), 'fallback')
})

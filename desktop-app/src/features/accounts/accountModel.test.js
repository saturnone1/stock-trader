import test from 'node:test'
import assert from 'node:assert/strict'
import {
  brokerOptionsFromMetadata,
  createAccountForm,
  normalizeAccountsResponse,
  projectAccountError,
  selectBroker,
} from './accountModel.js'

const brokers = [
  {
    type: 'Alpaca',
    defaultEnvironment: 'Paper',
    environments: ['Paper', 'Live'],
    isImplemented: true,
  },
  {
    type: 'LsSecurities',
    defaultEnvironment: 'Virtual',
    environments: ['Virtual', 'Real'],
    isImplemented: true,
  },
]

test('account form and broker changes use server-owned defaults', () => {
  const initial = createAccountForm(brokers)
  assert.equal(initial.brokerType, 'Alpaca')
  assert.equal(initial.environment, 'Paper')

  const changed = selectBroker(initial, brokers, 'LsSecurities')
  assert.equal(changed.brokerType, 'LsSecurities')
  assert.equal(changed.environment, 'Virtual')
  assert.equal(initial.environment, 'Paper')
})

test('account projections accept explicit camel-case contracts and legacy casing', () => {
  assert.deepEqual(normalizeAccountsResponse({ accounts: [{ id: 1 }] }), [{ id: 1 }])
  assert.deepEqual(normalizeAccountsResponse({ Accounts: [{ id: 2 }] }), [{ id: 2 }])
  assert.deepEqual(brokerOptionsFromMetadata({ brokers }), brokers)
  assert.deepEqual(normalizeAccountsResponse({ accounts: 'invalid' }), [])
})

test('account API errors retain server validation details', () => {
  assert.equal(
    projectAccountError({ response: { data: { errors: ['one', 'two'] } } }, 'fallback'),
    'one two',
  )
  assert.equal(projectAccountError({}, 'fallback'), 'fallback')
})

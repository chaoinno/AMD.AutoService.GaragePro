import assert from 'node:assert/strict'
import test from 'node:test'
import { initialSignupFlow, persistSignupFlow, resendSecondsRemaining, restoreSignupFlow, SIGNUP_STORAGE_KEY, signupReducer } from '../src/features/signup/signupFlow.ts'

const now = Date.parse('2026-10-14T00:00:00Z')
const ticket = { signupToken: 'verified-token', expiresAt: '2026-10-14T00:10:00Z' }
const sent = () => signupReducer(initialSignupFlow, { type: 'emailStarted', email: 'owner@gmail.com', resendAfterSeconds: 60, now })
function storage() {
  const values = new Map()
  return { getItem: (key) => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: (key) => values.delete(key) }
}

test('email signup transitions account → OTP → workspace → done', () => {
  const otp = sent()
  assert.equal(otp.step, 'otp')
  assert.equal(otp.ticket, 'sent')
  assert.equal(otp.email, 'owner@gmail.com')
  const workspace = signupReducer(otp, { type: 'verified', ...ticket })
  assert.equal(workspace.step, 'workspace')
  assert.equal(workspace.ticket, 'verified')
  assert.equal(workspace.signupToken, ticket.signupToken)
  const done = signupReducer(workspace, { type: 'completed' })
  assert.equal(done.step, 'done')
  assert.equal(done.signupToken, undefined)
})
test('Google verification goes directly to workspace and invalid transitions are ignored', () => {
  const workspace = signupReducer(initialSignupFlow, { type: 'verified', ...ticket })
  assert.equal(workspace.step, 'workspace')
  assert.equal(signupReducer(initialSignupFlow, { type: 'completed' }), initialSignupFlow)
  assert.equal(signupReducer(workspace, { type: 'emailStarted', email: 'new@gmail.com', resendAfterSeconds: 0, now }), workspace)
})
test('restart and changeEmail clear token, request ID and cooldown while retaining email', () => {
  const workspace = signupReducer(sent(), { type: 'verified', ...ticket })
  for (const type of ['restart', 'changeEmail']) {
    const state = signupReducer(workspace, { type })
    assert.deepEqual(state, { ...initialSignupFlow, email: 'owner@gmail.com' })
  }
})
test('resend availability rounds up, expires, and moves when another code is sent', () => {
  const otp = sent()
  assert.equal(resendSecondsRemaining(otp, now), 60)
  assert.equal(resendSecondsRemaining(otp, now + 59_001), 1)
  assert.equal(resendSecondsRemaining(otp, now + 60_000), 0)
  assert.equal(resendSecondsRemaining(otp, now + 90_000), 0)
  const resent = signupReducer(otp, { type: 'resendScheduled', resendAfterSeconds: 30, now: now + 60_000 })
  assert.equal(resendSecondsRemaining(resent, now + 60_000), 30)
})
test('persistence restores OTP/token and request IDs and never stores passwords or extra fields', () => {
  const store = storage()
  const otp = signupReducer(sent(), { type: 'edited', requestId: 'same-retry-id' })
  persistSignupFlow({ ...otp, password: 'DO-NOT-STORE', sampleTechnicians: [{ password: 'DO-NOT-STORE' }], idToken: 'DO-NOT-STORE' }, store)
  assert.equal(store.getItem(SIGNUP_STORAGE_KEY).includes('DO-NOT-STORE'), false)
  assert.deepEqual(restoreSignupFlow(store, now), otp)
  const workspace = signupReducer(otp, { type: 'verified', ...ticket })
  persistSignupFlow(workspace, store)
  assert.deepEqual(restoreSignupFlow(store, now), { ...workspace, resendAvailableAt: 0 })
  assert.equal(restoreSignupFlow(store, now + 600_000).step, 'account')
  persistSignupFlow(signupReducer(workspace, { type: 'completed' }), store)
  assert.equal(store.getItem(SIGNUP_STORAGE_KEY), null)
})
test('malformed and unavailable storage never break signup', () => {
  const store = storage()
  for (const raw of ['bad-json', 'null', '{}', '{"step":"otp","email":"x","resendAvailableAt":"invalid"}']) {
    store.setItem(SIGNUP_STORAGE_KEY, raw)
    assert.equal(restoreSignupFlow(store, now).step, 'account')
  }
  const blocked = { getItem() { throw new Error('blocked') }, setItem() { throw new Error('blocked') }, removeItem() { throw new Error('blocked') } }
  assert.doesNotThrow(() => persistSignupFlow(sent(), blocked))
  assert.deepEqual(restoreSignupFlow(blocked, now), initialSignupFlow)
  assert.deepEqual(restoreSignupFlow(null, now), initialSignupFlow)
})

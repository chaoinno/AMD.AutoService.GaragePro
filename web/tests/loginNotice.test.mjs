import assert from 'node:assert/strict'
import test from 'node:test'
import {
  LOGIN_NOTICE_KEY,
  LOGIN_NOTICE_MAX_AGE_MS,
  parseLoginNotice,
  stashLoginNotice,
  takeLoginNotice,
  peekLoginNotice,
  clearLoginNotice,
} from '../src/lib/loginNotice.ts'

function memoryStorage() {
  const map = new Map()
  return {
    getItem: (key) => (map.has(key) ? map.get(key) : null),
    setItem: (key, value) => map.set(key, String(value)),
    removeItem: (key) => map.delete(key),
    size: () => map.size,
  }
}

const notice = { code: 'AUTH_REQUIRED', messageTh: 'แพ็กเกจของอู่เปลี่ยนแล้ว กรุณาเข้าสู่ระบบใหม่', traceId: 'trace-1' }
const stashedAt = new Date('2026-10-13T03:00:00Z')

test('stash then take returns the notice once and removes it from storage', () => {
  const storage = memoryStorage()
  stashLoginNotice(notice, storage, stashedAt)
  assert.equal(storage.size(), 1)
  assert.equal(typeof storage.getItem(LOGIN_NOTICE_KEY), 'string')

  const taken = takeLoginNotice(storage, new Date(stashedAt.getTime() + 5_000))
  assert.deepEqual(taken, { ...notice, at: stashedAt.toISOString() })
  assert.equal(storage.size(), 0)
  assert.equal(takeLoginNotice(storage, stashedAt), null)
})

test('a notice older than the max age is dropped (and still removed)', () => {
  const storage = memoryStorage()
  stashLoginNotice(notice, storage, stashedAt)
  const later = new Date(stashedAt.getTime() + LOGIN_NOTICE_MAX_AGE_MS + 1)
  assert.equal(takeLoginNotice(storage, later), null)
  assert.equal(storage.size(), 0)

  stashLoginNotice(notice, storage, stashedAt)
  const justInTime = new Date(stashedAt.getTime() + LOGIN_NOTICE_MAX_AGE_MS)
  assert.equal(takeLoginNotice(storage, justInTime)?.code, 'AUTH_REQUIRED')
})

test('malformed or incomplete payloads are ignored instead of throwing', () => {
  assert.equal(parseLoginNotice(null), null)
  assert.equal(parseLoginNotice(''), null)
  assert.equal(parseLoginNotice('{not json'), null)
  assert.equal(parseLoginNotice('"string"'), null)
  assert.equal(parseLoginNotice(JSON.stringify({ code: 'X' })), null)
  assert.deepEqual(parseLoginNotice(JSON.stringify({ code: 'X', messageTh: 'ข้อความ', traceId: 7 })), {
    code: 'X',
    messageTh: 'ข้อความ',
    traceId: undefined,
    at: undefined,
  })

  const storage = memoryStorage()
  storage.setItem(LOGIN_NOTICE_KEY, '{broken')
  assert.equal(takeLoginNotice(storage), null)
  assert.equal(storage.size(), 0)
})

test('missing or throwing storage never breaks the caller', () => {
  assert.doesNotThrow(() => stashLoginNotice(notice, null))
  assert.equal(takeLoginNotice(null), null)

  const throwing = {
    getItem: () => {
      throw new Error('blocked')
    },
    setItem: () => {
      throw new Error('quota')
    },
    removeItem: () => {
      throw new Error('blocked')
    },
  }
  assert.doesNotThrow(() => stashLoginNotice(notice, throwing))
  assert.equal(takeLoginNotice(throwing), null)
})

test('peek keeps the notice for the second login mount until it is cleared', () => {
  const storage = memoryStorage()
  const at = new Date('2026-10-14T06:20:00Z')
  stashLoginNotice({ code: 'AUTH_REQUIRED', messageTh: 'แพ็กเกจของอู่เปลี่ยนแล้ว กรุณาเข้าสู่ระบบใหม่' }, storage, at)
  assert.equal(peekLoginNotice(storage, at)?.code, 'AUTH_REQUIRED', 'SPA mount')
  assert.equal(peekLoginNotice(storage, at)?.code, 'AUTH_REQUIRED', 'hard-reload mount still sees it')
  clearLoginNotice(storage)
  assert.equal(peekLoginNotice(storage, at), null)
  assert.equal(peekLoginNotice(null), null)
})

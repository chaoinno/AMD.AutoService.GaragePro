import assert from 'node:assert/strict'
import test from 'node:test'
import {
  FEATURES,
  FEATURE_KEYS,
  daysLeftOf,
  featureForPath,
  formatDaysLeft,
  isFeatureKey,
  isFeatureLocked,
  lockedFeatureForPath,
  lockedFeatures,
  PAID_WARN_DAYS,
  trialTone,
} from '../src/lib/plan.ts'

const trial = (overrides = {}) => ({ kind: 'trial', readOnly: false, features: [], ...overrides })
const paid = (overrides = {}) => ({ kind: 'paid', readOnly: false, features: [], ...overrides })
const now = new Date('2026-10-13T03:00:00Z')

test('registry keys match the backend feature strings and every path belongs to exactly one feature', () => {
  assert.deepEqual(
    [...FEATURE_KEYS].sort(),
    ['advancedReports', 'export', 'inventory', 'multiBranch', 'pos', 'purchasing', 'taxInvoice'],
  )
  for (const key of FEATURE_KEYS) {
    const feature = FEATURES[key]
    assert.equal(feature.key, key)
    assert.ok(feature.label.length > 0 && feature.description.length > 0 && feature.bullets.length > 0, key)
    for (const path of feature.paths) assert.ok(path.startsWith('/') && !path.endsWith('/'), `${key}: ${path}`)
  }
  const allPaths = FEATURE_KEYS.flatMap((key) => FEATURES[key].paths)
  assert.equal(new Set(allPaths).size, allPaths.length)
  assert.equal(isFeatureKey('purchasing'), true)
  assert.equal(isFeatureKey('toString'), false)
  assert.equal(isFeatureKey('jobs'), false)
})

test('isFeatureLocked never locks without a plan or on legacy/paid, and locks what a trial was not granted', () => {
  assert.equal(isFeatureLocked(undefined, 'purchasing'), false)
  assert.equal(isFeatureLocked({ kind: 'legacy', readOnly: false, features: [] }, 'purchasing'), false)
  assert.equal(isFeatureLocked({ kind: 'paid', readOnly: false, features: [] }, 'pos'), false)
  assert.equal(isFeatureLocked(trial(), 'purchasing'), true)
  assert.equal(isFeatureLocked(trial({ features: ['purchasing'] }), 'purchasing'), false)
  assert.equal(isFeatureLocked(trial({ features: ['purchasing'] }), 'inventory'), true)
  assert.equal(isFeatureLocked({ kind: 'suspended', readOnly: true, features: [] }, 'export'), true)
  // features หายจาก JSON (ไม่ควรเกิด) ถือว่าล็อกทั้งหมด ไม่โยน error
  assert.equal(isFeatureLocked({ kind: 'trial', readOnly: false }, 'export'), true)
  assert.deepEqual(lockedFeatures(trial({ features: ['export', 'pos'] })).map((f) => f.key), [
    'purchasing',
    'inventory',
    'taxInvoice',
    'advancedReports',
    'multiBranch',
  ])
  assert.deepEqual(lockedFeatures(undefined), [])
})

test('lockedFeatureForPath matches by path prefix on segment boundaries only', () => {
  assert.equal(featureForPath('/purchasing/po')?.key, 'purchasing')
  assert.equal(featureForPath('/suppliers')?.key, 'purchasing')
  assert.equal(featureForPath('/sales/42')?.key, 'pos')
  assert.equal(featureForPath('/reports/work-intervals')?.key, 'advancedReports')
  assert.equal(featureForPath('/sales-report'), null)
  assert.equal(featureForPath('/reports/dashboard'), null)
  assert.equal(featureForPath('/jobs'), null)

  assert.equal(lockedFeatureForPath(trial(), '/purchasing/pr')?.key, 'purchasing')
  assert.equal(lockedFeatureForPath(trial({ features: ['purchasing'] }), '/purchasing/pr'), null)
  assert.equal(lockedFeatureForPath(undefined, '/purchasing'), null)
  assert.equal(lockedFeatureForPath({ kind: 'paid', readOnly: false, features: [] }, '/inventory'), null)
  assert.equal(lockedFeatureForPath(trial(), '/reports/vehicle-history'), null)
})

test('trialTone: none for legacy/no plan · trial warns at 7 days or less · expired when read-only or suspended', () => {
  assert.equal(trialTone(undefined, now), 'none')
  assert.equal(trialTone({ kind: 'legacy', readOnly: false, features: [] }, now), 'none')
  assert.equal(trialTone({ kind: 'paid', readOnly: false, features: [] }, now), 'none')
  assert.equal(trialTone(trial({ daysLeft: 23 }), now), 'neutral')
  assert.equal(trialTone(trial({ daysLeft: 8 }), now), 'neutral')
  assert.equal(trialTone(trial({ daysLeft: 7 }), now), 'warn')
  assert.equal(trialTone(trial({ daysLeft: 0 }), now), 'warn')
  assert.equal(trialTone(trial({ daysLeft: 3, readOnly: true }), now), 'expired')
  assert.equal(trialTone({ kind: 'suspended', readOnly: true, features: [] }, now), 'expired')
  assert.equal(trialTone({ kind: 'suspended', readOnly: false, features: [] }, now), 'expired')
  // ไม่มี daysLeft จาก server → คำนวณจาก trialEndsAt (ค่าไม่มีโซนเวลาตีความเป็น UTC)
  assert.equal(trialTone(trial({ trialEndsAt: '2026-10-18T03:00:00' }), now), 'warn')
  assert.equal(trialTone(trial({ trialEndsAt: '2026-11-12T03:00:00Z' }), now), 'neutral')
  assert.equal(trialTone(trial(), now), 'neutral')
})

test('daysLeftOf prefers the server value, falls back to trialEndsAt, and formats Thai text', () => {
  assert.equal(daysLeftOf(trial({ daysLeft: 12, trialEndsAt: '2026-10-14T00:00:00Z' }), now), 12)
  assert.equal(daysLeftOf(trial({ daysLeft: -2 }), now), 0)
  assert.equal(daysLeftOf(trial({ trialEndsAt: '2026-10-14T00:00:00Z' }), now), 1)
  assert.equal(daysLeftOf(trial({ trialEndsAt: '2026-10-12T00:00:00Z' }), now), 0)
  assert.equal(daysLeftOf(trial({ trialEndsAt: 'not-a-date' }), now), null)
  assert.equal(daysLeftOf(trial(), now), null)
  assert.equal(daysLeftOf({ kind: 'paid', readOnly: false, features: [] }, now), null, 'paid without an end date has no countdown')
  assert.equal(daysLeftOf(undefined, now), null)

  assert.equal(formatDaysLeft(23), 'เหลือ 23 วัน')
  assert.equal(formatDaysLeft(1), 'เหลือ 1 วัน')
  assert.equal(formatDaysLeft(0), 'หมดอายุวันนี้')
  assert.equal(formatDaysLeft(null), 'ไม่ทราบวันหมดอายุ')
  assert.equal(formatDaysLeft(undefined), 'ไม่ทราบวันหมดอายุ')
})

test('trialTone for paid plans: hidden normally, warns at 14 days or less, expired when read-only (docs/17 §6.1)', () => {
  assert.equal(PAID_WARN_DAYS, 14)
  // ไม่มีวันหมดอายุ หรือยังเหลือนาน = ไม่แสดงแถบ (พฤติกรรมเดิม)
  assert.equal(trialTone(paid(), now), 'none')
  assert.equal(trialTone(paid({ daysLeft: 60 }), now), 'none')
  assert.equal(trialTone(paid({ daysLeft: 15 }), now), 'none')
  assert.equal(trialTone(paid({ daysLeft: 14 }), now), 'warn')
  assert.equal(trialTone(paid({ daysLeft: 8 }), now), 'warn', 'paid warns earlier than trial (8 > 7)')
  assert.equal(trialTone(paid({ daysLeft: 0 }), now), 'warn')
  // เดิมคืน none ก่อนตรวจ readOnly → อู่ที่จ่ายแล้วกลายเป็นอ่านอย่างเดียวโดยไม่มีคำอธิบาย
  assert.equal(trialTone(paid({ readOnly: true }), now), 'expired')
  assert.equal(trialTone(paid({ daysLeft: -3, readOnly: true }), now), 'expired')
  // server ไม่ส่ง daysLeft → คำนวณจาก trialEndsAt (server ส่งชื่อฟิลด์นี้ให้ paid ด้วย)
  assert.equal(trialTone(paid({ trialEndsAt: '2026-10-20T17:00:00Z' }), now), 'warn')
  assert.equal(trialTone(paid({ trialEndsAt: '2026-12-31T17:00:00Z' }), now), 'none')
})

test('daysLeftOf counts down paid plans that have an end date, never suspended/legacy', () => {
  assert.equal(daysLeftOf(paid({ daysLeft: 10 }), now), 10)
  assert.equal(daysLeftOf(paid({ daysLeft: -4 }), now), 0)
  assert.equal(daysLeftOf(paid({ trialEndsAt: '2026-10-14T00:00:00Z' }), now), 1)
  assert.equal(daysLeftOf({ kind: 'suspended', readOnly: true, features: [], daysLeft: 5 }, now), null)
  assert.equal(daysLeftOf({ kind: 'legacy', readOnly: false, features: [], daysLeft: 5 }, now), null)
})

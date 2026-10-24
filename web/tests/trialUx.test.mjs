import assert from 'node:assert/strict'
import test from 'node:test'
import {
  checklistProgress,
  credentialsBelongTo,
  deriveChecklist,
  extensionButtonLabel,
  featureInterestNote,
  formatLimitUsage,
  isExtensionRecentlyRequested,
  isSelfServePlan,
  limitUsageItems,
  lockedActionReason,
  lockedNavLabel,
  parseSampleCredentials,
  STAFF_ROLE_OPTIONS,
  trialBannerCopy,
  trialBannerLabel,
  welcomeDismissKey,
} from '../src/features/plan/trialUx.ts'
import { daysLeftOf, featureForPath, formatDaysLeft, lockedFeatureForPath, trialTone } from '../src/lib/plan.ts'

const trial = (overrides = {}) => ({ kind: 'trial', readOnly: false, features: [], ...overrides })
const now = new Date('2026-10-14T03:00:00Z')
const bannerFor = (plan) => trialBannerCopy(
  trialTone(plan, now),
  formatDaysLeft(daysLeftOf(plan, now)),
  plan?.kind === 'suspended',
  plan?.kind === 'paid',
)

// ---- เช็คลิสต์ --------------------------------------------------------------------------------------

test('checklist ticks each step only from real data, the owner alone does not count as the first technician', () => {
  const fresh = deriveChecklist({ checklist: { staffCount: 1, catalogCount: 0, jobCount: 0, quotationSentCount: 0, mobileLoginSeen: false } })
  assert.deepEqual(fresh.map((item) => item.key), ['staff', 'catalog', 'job', 'quotation', 'mobile'])
  assert.deepEqual(fresh.map((item) => item.done), [false, false, false, false, false])
  assert.deepEqual(checklistProgress(fresh), { done: 0, total: 5 })

  const sample = deriveChecklist({ checklist: { staffCount: 3, catalogCount: 42, jobCount: 2, quotationSentCount: 1, mobileLoginSeen: true } })
  assert.deepEqual(sample.map((item) => item.done), [true, true, true, true, true])
  assert.deepEqual(checklistProgress(sample), { done: 5, total: 5 })
})

test('checklist treats fields the API dropped (null → absent) as not done instead of crashing', () => {
  for (const status of [undefined, null, {}, { checklist: {} }, { checklist: { mobileLoginSeen: 'yes' } }]) {
    const items = deriveChecklist(status)
    assert.equal(items.length, 5)
    assert.ok(items.every((item) => item.done === false), JSON.stringify(status))
  }
})

test('checklist actions point somewhere real: staff/catalog links, job opens the create modal, quotation follows job state', () => {
  const noJob = deriveChecklist({ checklist: { jobCount: 0 } })
  const byKey = Object.fromEntries(noJob.map((item) => [item.key, item]))
  assert.deepEqual(byKey.staff.action, { kind: 'link', to: '/staffs', label: 'ไปทำ' })
  assert.deepEqual(byKey.catalog.action, { kind: 'link', to: '/products', label: 'ไปทำ' })
  assert.equal(byKey.job.action.kind, 'createJob')
  assert.equal(byKey.quotation.action.kind, 'createJob', 'no job yet → the next step is opening one')
  assert.equal(byKey.mobile.action.kind, 'mobileApp')

  const withJob = Object.fromEntries(deriveChecklist({ checklist: { jobCount: 1 } }).map((item) => [item.key, item]))
  assert.deepEqual(withJob.quotation.action, { kind: 'link', to: '/jobs?view=board', label: 'ไปทำ' })
})

test('limit usage renders "พนักงาน 2/5 · สินค้า 42/200 · ลูกค้า 2/200 · จ๊อบ 2/100" and flags full limits', () => {
  const limits = {
    staff: { used: 2, max: 5 },
    catalog: { used: 42, max: 200 },
    customers: { used: 2, max: 200 },
    jobs: { used: 2, max: 100 },
  }
  assert.equal(formatLimitUsage(limits), 'พนักงาน 2/5 · สินค้า 42/200 · ลูกค้า 2/200 · จ๊อบ 2/100')
  assert.equal(formatLimitUsage({ ...limits, catalog: { used: 1200, max: 2000 } }).includes('สินค้า 1,200/2,000'), true)
  assert.deepEqual(limitUsageItems({ staff: { used: 5, max: 5 } }).map((item) => item.full), [true])
  assert.equal(formatLimitUsage(undefined), '')
  assert.equal(formatLimitUsage({ jobs: { used: 3 } }), '', 'a limit without max is not shown as "3/undefined"')
})

// ---- แถบทดลองใช้ ------------------------------------------------------------------------------------

test('banner: legacy/no plan/paid far from expiry render nothing; trial is neutral, ≤7 days warns, read-only/suspended is expired', () => {
  assert.equal(bannerFor(undefined), null)
  assert.equal(bannerFor({ kind: 'legacy', readOnly: false, features: [] }), null)
  assert.equal(bannerFor({ kind: 'paid', readOnly: false, features: [] }), null)
  assert.equal(bannerFor({ kind: 'paid', readOnly: false, features: [], daysLeft: 30 }), null)

  assert.equal(trialTone(trial({ daysLeft: 23 }), now), 'neutral')
  assert.equal(bannerFor(trial({ daysLeft: 23 })).title, 'ทดลองใช้ · เหลือ 23 วัน')

  assert.equal(trialTone(trial({ daysLeft: 7 }), now), 'warn')
  const warn = bannerFor(trial({ daysLeft: 7 }))
  assert.equal(warn.title, 'ทดลองใช้ · เหลือ 7 วัน')
  assert.match(warn.detail, /ใกล้ครบช่วงทดลองใช้/)
  assert.equal(bannerFor(trial({ daysLeft: 0 })).title, 'ทดลองใช้ · หมดอายุวันนี้')

  const expired = bannerFor(trial({ daysLeft: -3, readOnly: true }))
  assert.equal(expired.title, 'ช่วงทดลองใช้ครบ 30 วันแล้ว ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้')
  assert.match(bannerFor({ kind: 'suspended', readOnly: true, features: [] }).title, /ถูกระงับ/)
})

test('banner for paid plans: ≤14 days warns with payment copy, read-only explains and points to payment', () => {
  const paid = (overrides = {}) => ({ kind: 'paid', readOnly: false, features: [], ...overrides })

  const warn = bannerFor(paid({ daysLeft: 10 }))
  assert.equal(warn.title, 'แพ็กเกจใกล้หมดอายุ · เหลือ 10 วัน')
  assert.match(warn.detail, /แจ้งชำระเงิน/)
  assert.doesNotMatch(warn.title + warn.detail, /ทดลองใช้/, 'paid copy must not talk about the trial')
  assert.equal(bannerFor(paid({ daysLeft: 0 })).title, 'แพ็กเกจใกล้หมดอายุ · หมดอายุวันนี้')

  const expired = bannerFor(paid({ daysLeft: -2, readOnly: true }))
  assert.equal(expired.title, 'แพ็กเกจหมดอายุแล้ว ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้')
  assert.match(expired.detail, /แจ้งชำระเงิน/)
  assert.doesNotMatch(expired.title + expired.detail, /ทดลองใช้/)

  // trial ใกล้หมด/หมดแล้วก็ชวนแจ้งชำระเงินด้วย (ปุ่ม "แจ้งชำระเงิน" บนแถบ)
  assert.match(bannerFor(trial({ daysLeft: 3 })).detail, /แจ้งชำระเงิน/)
  assert.match(bannerFor(trial({ readOnly: true })).detail, /แจ้งชำระเงิน/)

  // ถูกระงับใช้ข้อความระงับเสมอ แม้ไม่ใช่ trial
  assert.match(trialBannerCopy('expired', 'x', true, true).title, /ถูกระงับ/)
  assert.equal(trialBannerLabel(true), 'สถานะแพ็กเกจ')
  assert.equal(trialBannerLabel(false), 'สถานะแพ็กเกจทดลองใช้')
  assert.equal(extensionButtonLabel(false), 'ขอต่ออายุ / สมัครแพ็กเกจ')
  assert.equal(extensionButtonLabel(true), 'ขอให้ทีมงานติดต่อ')
})

test('extension request counts as sent for 24 hours (backend allows one per day)', () => {
  assert.equal(isExtensionRecentlyRequested(undefined, now), false)
  assert.equal(isExtensionRecentlyRequested('2026-10-14T01:00:00Z', now), true)
  assert.equal(isExtensionRecentlyRequested('2026-10-13T01:00:00', now), false, 'zone-less API value is UTC → 26h ago')
  assert.equal(isExtensionRecentlyRequested('not a date', now), false)
  assert.equal(featureInterestNote('จัดซื้อ'), 'สนใจฟีเจอร์: จัดซื้อ')
})

// ---- เมนู/route ที่ล็อก ------------------------------------------------------------------------------

// path ทุกตัวใน navGroups ของ AppShell — กุญแจในเมนูและ FeatureGate ใช้ lockedFeatureForPath ตัวเดียวกัน
const NAV_EXPECTATIONS = {
  '/jobs': null,
  '/purchasing/pr': 'purchasing',
  '/purchasing/po': 'purchasing',
  '/inventory': 'inventory',
  '/sales': 'pos',
  '/reports/dashboard': null,
  '/reports/cycle-time': 'advancedReports',
  '/reports/sales-margin': 'advancedReports',
  '/reports/retail-sales': 'advancedReports',
  '/reports/stock': 'advancedReports',
  '/reports/vehicle-history': null,
  '/reports/service-due': null,
  '/reports/work-intervals': 'advancedReports',
  '/customers': null,
  '/vehicles': null,
  '/staffs': null,
  '/products': null,
  '/suppliers': 'purchasing',
  '/warehouses': 'inventory',
  '/catalog-categories': null,
  '/quotation-templates': null,
  '/promotions': 'pos',
}

test('every sidebar path maps to the expected feature, and a trial locks exactly those', () => {
  for (const [path, expected] of Object.entries(NAV_EXPECTATIONS)) {
    assert.equal(featureForPath(path)?.key ?? null, expected, path)
    assert.equal(lockedFeatureForPath(trial(), path)?.key ?? null, expected, `trial ${path}`)
    assert.equal(lockedFeatureForPath(undefined, path), null, `legacy ${path}`)
    assert.equal(lockedFeatureForPath({ kind: 'paid', readOnly: false, features: [] }, path), null, `paid ${path}`)
  }
  // หน้าลูกของฟีเจอร์ที่ล็อกถูกล็อกด้วย (เปิดบิลขายจากลิงก์ตรง)
  assert.equal(lockedFeatureForPath(trial(), '/sales/123')?.key, 'pos')
  // trial ที่ได้ฟีเจอร์เพิ่ม (ทีมขายเปิดให้) ปลดล็อกเฉพาะฟีเจอร์นั้น
  assert.equal(lockedFeatureForPath(trial({ features: ['advancedReports'] }), '/reports/stock'), null)
  assert.equal(lockedFeatureForPath(trial({ features: ['advancedReports'] }), '/inventory')?.key, 'inventory')
})

test('locked nav items announce the lock in their accessible name; locked buttons give a readable reason', () => {
  assert.equal(lockedNavLabel('ใบขอซื้อ (PR)'), 'ใบขอซื้อ (PR) (ล็อกในแพ็กเกจทดลองใช้)')
  assert.equal(lockedActionReason('ส่งออก Excel/CSV'), 'ส่งออก Excel/CSV เปิดในแพ็กเกจเต็ม')
})

// ---- การ์ดต้อนรับ/รหัสช่าง/พนักงาน -------------------------------------------------------------------

test('self-serve detection: only non-legacy plans get the simple staff form and trial UI', () => {
  assert.equal(isSelfServePlan(undefined), false)
  assert.equal(isSelfServePlan({ kind: 'legacy', readOnly: false, features: [] }), false)
  assert.equal(isSelfServePlan(trial()), true)
  assert.equal(isSelfServePlan({ kind: 'paid', readOnly: false, features: [] }), true)
})

test('sample technician credentials from the signup page are parsed defensively', () => {
  const raw = JSON.stringify({ workspaceNo: 'SP1042', technicians: [{ userName: 'SP1042.T01', password: 'a1b2c3d4e5' }, { userName: '', password: 'x' }] })
  assert.deepEqual(parseSampleCredentials(raw), { workspaceNo: 'SP1042', technicians: [{ userName: 'SP1042.T01', password: 'a1b2c3d4e5' }] })
  assert.equal(parseSampleCredentials(null), null)
  assert.equal(parseSampleCredentials('{broken'), null)
  assert.equal(parseSampleCredentials(JSON.stringify({ workspaceNo: 'SP1', technicians: [] })), null)
  assert.equal(parseSampleCredentials(JSON.stringify({ technicians: [{ userName: 'SP1.T01', password: 'p' }] }))?.workspaceNo, '')

  const creds = parseSampleCredentials(raw)
  assert.equal(credentialsBelongTo(creds, 'SP1042'), true)
  assert.equal(credentialsBelongTo(creds, 'SP2000'), false, 'another workspace in the same tab must not see these')
  assert.equal(credentialsBelongTo(creds, undefined), true)
  assert.equal(welcomeDismissKey('SP1042'), 'garagepro.web.welcomeDismissed.SP1042')
})

test('staff role options are the backend UserRole names, technician first', () => {
  assert.deepEqual(STAFF_ROLE_OPTIONS.map((option) => option.value), ['Technician', 'Lead', 'FrontDesk', 'Office', 'Cashier', 'Manager'])
  assert.deepEqual(STAFF_ROLE_OPTIONS.map((option) => option.label), ['ช่าง', 'หัวหน้าช่าง', 'หน้าร้าน', 'ธุรการ', 'แคชเชียร์', 'ผู้จัดการ'])
})
